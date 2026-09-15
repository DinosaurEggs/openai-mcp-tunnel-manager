using System.Diagnostics;
using System.Text.Json;
using OpenAITunnelManager.Core.Abstractions;
using OpenAITunnelManager.Core.Models;

namespace OpenAITunnelManager.Infrastructure.TunnelClient;

public sealed class TunnelClientService(TunnelClientOptions options) : ITunnelClientService
{
    public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["--version"], false, cancellationToken).ConfigureAwait(false);
        return result.StandardOutput.Trim();
    }

    public async Task<IReadOnlyList<TunnelConnection>> GetConnectionsAsync(CancellationToken cancellationToken = default)
    {
        using var profilesJson = await RunJsonAsync(["profiles", "list", "--json"], cancellationToken).ConfigureAwait(false);
        using var runtimesJson = await RunJsonAsync(["runtimes", "list", "--json"], cancellationToken).ConfigureAwait(false);
        var profiles = ParseProfiles(profilesJson.RootElement);
        var profilesByName = profiles.ToDictionary(static p => p.Name, StringComparer.OrdinalIgnoreCase);
        var linkedProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<TunnelConnection>();

        foreach (var runtime in ParseRuntimes(runtimesJson.RootElement))
        {
            profilesByName.TryGetValue(runtime.ProfileName, out var listed);
            if (listed is not null && !string.IsNullOrWhiteSpace(runtime.ProfilePath) && !SamePath(listed.Path, runtime.ProfilePath)) listed = null;
            if (listed is not null) linkedProfiles.Add(listed.Name);

            var profilePath = listed?.Path ?? runtime.ProfilePath;
            var profileName = !string.IsNullOrWhiteSpace(runtime.ProfileName) ? runtime.ProfileName : listed?.Name ?? string.Empty;
            var metadata = ProfileMetadataReader.Read(profilePath);
            var status = await ReadStatusAsync(runtime.Alias, cancellationToken).ConfigureAwait(false);
            var target = ResolveTarget(status.Raw, metadata);
            var effectiveProfilePath = FirstNonEmpty(listed?.Path, status.ProfilePath, runtime.ProfilePath);

            result.Add(new TunnelConnection(
                Name: runtime.Alias,
                ProfileName: profileName,
                ProfilePath: effectiveProfilePath,
                ProfileListed: listed is not null,
                RuntimeAlias: runtime.Alias,
                RuntimeProfileName: runtime.ProfileName,
                RuntimeProfilePath: FirstNonEmpty(status.ProfilePath, runtime.ProfilePath),
                TunnelId: FirstNonEmpty(status.TunnelId, runtime.TunnelId, metadata.TunnelId),
                TargetKind: target.Kind,
                TargetValue: target.Value,
                State: status.State,
                ProcessRunning: status.ProcessRunning,
                Healthy: status.Healthy,
                Ready: status.Ready,
                HealthUrl: status.HealthUrl,
                HealthDetailsUrl: status.HealthDetailsUrl,
                McpHealthUrl: status.McpHealthUrl,
                LogPath: status.LogPath,
                ProcessId: status.ProcessId,
                Error: status.ErrorMessage));
        }

        foreach (var profile in profiles)
        {
            if (linkedProfiles.Contains(profile.Name)) continue;
            var metadata = ProfileMetadataReader.Read(profile.Path);
            result.Add(new TunnelConnection(
                Name: profile.Name,
                ProfileName: profile.Name,
                ProfilePath: profile.Path,
                ProfileListed: true,
                RuntimeAlias: string.Empty,
                RuntimeProfileName: string.Empty,
                RuntimeProfilePath: string.Empty,
                TunnelId: metadata.TunnelId,
                TargetKind: metadata.TargetKind,
                TargetValue: metadata.TargetValue,
                State: RuntimeState.Configured,
                ProcessRunning: false,
                Healthy: false,
                Ready: false,
                HealthUrl: string.Empty,
                HealthDetailsUrl: string.Empty,
                McpHealthUrl: string.Empty,
                LogPath: string.Empty,
                ProcessId: null,
                Error: string.Empty));
        }

        return result.OrderBy(static x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static x => x.RuntimeAlias, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static x => x.ProfileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<TunnelConnection> GetStatusAsync(TunnelConnection connection, CancellationToken cancellationToken = default)
    {
        if (!connection.HasRuntime) return connection;
        var status = await ReadStatusAsync(connection.RuntimeAlias, cancellationToken).ConfigureAwait(false);
        var profilePath = FirstNonEmpty(connection.ProfilePath, connection.RuntimeProfilePath, status.ProfilePath);
        var metadata = ProfileMetadataReader.Read(profilePath);
        var target = ResolveTarget(status.Raw, metadata);
        return connection with
        {
            ProfilePath = FirstNonEmpty(connection.ProfilePath, status.ProfilePath),
            RuntimeProfilePath = FirstNonEmpty(status.ProfilePath, connection.RuntimeProfilePath),
            TunnelId = FirstNonEmpty(status.TunnelId, connection.TunnelId, metadata.TunnelId),
            TargetKind = target.Kind,
            TargetValue = target.Value,
            State = status.State,
            ProcessRunning = status.ProcessRunning,
            Healthy = status.Healthy,
            Ready = status.Ready,
            HealthUrl = status.HealthUrl,
            HealthDetailsUrl = status.HealthDetailsUrl,
            McpHealthUrl = status.McpHealthUrl,
            LogPath = status.LogPath,
            ProcessId = status.ProcessId,
            Error = status.ErrorMessage
        };
    }

    public async Task StopRuntimeAsync(string alias, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        await RunAsync(["runtimes", "stop", alias, "--json"], false, cancellationToken).ConfigureAwait(false);
    }

    private async Task<RuntimeSnapshot> ReadStatusAsync(string alias, CancellationToken cancellationToken)
    {
        var command = await RunAsync(["runtimes", "status", alias, "--json"], true, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(command.StandardOutput))
        {
            var error = FirstNonEmpty(command.StandardError.Trim(), $"Runtime {alias} 状态为空");
            return command.ExitCode == 0 || LooksLikeMissingAlias(error)
                ? RuntimeSnapshot.Stopped(alias, error)
                : RuntimeSnapshot.Failed(alias, error);
        }

        try
        {
            using var document = JsonDocument.Parse(command.StandardOutput);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return RuntimeSnapshot.Failed(alias, "Runtime 状态 JSON 根节点不是对象");
            var processRunning = GetBoolean(root, "process_running", "running");
            var healthy = GetBoolean(root, "healthy");
            var ready = GetBoolean(root, "ready");
            var runtimeState = GetString(root, "runtime_state", "state").ToLowerInvariant();
            var stale = GetBoolean(root, "stale") || runtimeState is "stale" or "stale_alias";
            var state = stale ? RuntimeState.Stale
                : ready && processRunning ? RuntimeState.Ready
                : processRunning && healthy ? RuntimeState.Running
                : processRunning ? RuntimeState.Starting
                : runtimeState is "error" or "failed" ? RuntimeState.Error
                : runtimeState is "stopped" or "disconnected" or "not_running" or "missing_profile" ? RuntimeState.Stopped
                : command.ExitCode == 0 ? RuntimeState.Stopped : RuntimeState.Error;
            var errorMessage = FirstNonEmpty(GetString(root, "error", "remote_error"), command.ExitCode == 0 ? string.Empty : command.StandardError.Trim());
            if (command.ExitCode != 0 && LooksLikeMissingAlias(errorMessage)) state = RuntimeState.Stopped;
            return new RuntimeSnapshot(
                alias,
                state,
                processRunning,
                healthy,
                ready,
                GetString(root, "tunnel_id"),
                FindString(root, "profile_path", "profile_file", "config_path"),
                FindString(root, "health_url", "health_base_url"),
                FindString(root, "health_details_url"),
                FindString(root, "mcp_health_url"),
                FindString(root, "log_path", "log_file", "log_file_path", "runtime_log", "logs_path", "logs"),
                FindInt32(root, "pid", "process_id"),
                errorMessage,
                root.Clone());
        }
        catch (JsonException)
        {
            var error = FirstNonEmpty(command.StandardError.Trim(), command.StandardOutput.Trim(), "Runtime 状态不是有效 JSON");
            return LooksLikeMissingAlias(error) ? RuntimeSnapshot.Stopped(alias, error) : RuntimeSnapshot.Failed(alias, error);
        }
    }

    private async Task<JsonDocument> RunJsonAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await RunAsync(arguments, false, cancellationToken).ConfigureAwait(false);
        try { return JsonDocument.Parse(result.StandardOutput); }
        catch (JsonException exception) { throw new InvalidOperationException($"tunnel-client 返回了无效 JSON：{string.Join(' ', arguments)}", exception); }
    }

    private async Task<CommandResult> RunAsync(IReadOnlyList<string> arguments, bool allowFailure, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = options.ResolveExecutablePath(),
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        options.ApplyChildEnvironment(startInfo);
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("无法启动 tunnel-client 进程");
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException($"无法启动 tunnel-client：{startInfo.FileName}", exception);
        }
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.CommandTimeout);
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"tunnel-client 命令执行超时：{string.Join(' ', arguments)}");
        }
        var result = new CommandResult(process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
        if (!allowFailure && result.ExitCode != 0)
            throw new InvalidOperationException($"tunnel-client 命令失败（退出码 {result.ExitCode}）：{FirstNonEmpty(result.StandardError.Trim(), result.StandardOutput.Trim(), "未知错误")}");
        return result;
    }

    private static IReadOnlyList<ProfileEntry> ParseProfiles(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("profiles list JSON 根节点不是数组");
        return root.EnumerateArray().Where(static item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new ProfileEntry(GetString(item, "name"), GetString(item, "path")))
            .Where(static item => !string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(item.Path)).ToArray();
    }

    private static IReadOnlyList<RuntimeEntry> ParseRuntimes(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !TryGetProperty(root, "aliases", out var aliases) || aliases.ValueKind != JsonValueKind.Array) return [];
        return aliases.EnumerateArray().Where(static item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new RuntimeEntry(
                GetString(item, "alias"),
                GetString(item, "profile_name"),
                FirstNonEmpty(GetString(item, "profile_path"), GetString(item, "config_path")),
                GetString(item, "tunnel_id")))
            .Where(static item => !string.IsNullOrWhiteSpace(item.Alias)).ToArray();
    }

    private static (string Kind, string Value) ResolveTarget(JsonElement status, ProfileMetadata metadata)
    {
        if (status.ValueKind == JsonValueKind.Object && TryGetProperty(status, "process", out var process) && process.ValueKind == JsonValueKind.Object)
        {
            var kind = GetString(process, "target_kind");
            var value = GetString(process, "target_value");
            if (!string.IsNullOrWhiteSpace(kind) && !string.IsNullOrWhiteSpace(value)) return (kind, value);
        }
        return (metadata.TargetKind, metadata.TargetValue);
    }

    private static string GetString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(element, name, out var value)) continue;
            return value.ValueKind switch { JsonValueKind.String => value.GetString()?.Trim() ?? string.Empty, JsonValueKind.Number => value.GetRawText(), _ => string.Empty };
        }
        return string.Empty;
    }

    private static bool GetBoolean(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(element, name, out var value)) continue;
            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) return value.GetBoolean();
            if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed)) return parsed;
        }
        return false;
    }

    private static string FindString(JsonElement element, params string[] keys)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (keys.Any(key => string.Equals(key, property.Name, StringComparison.OrdinalIgnoreCase)) && property.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.Value.GetString())) return property.Value.GetString()!.Trim();
                var nested = FindString(property.Value, keys);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                var nested = FindString(child, keys);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        }
        return string.Empty;
    }

    private static int? FindInt32(JsonElement element, params string[] keys)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (keys.Any(key => string.Equals(key, property.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var number)) return number;
                    if (property.Value.ValueKind == JsonValueKind.String && int.TryParse(property.Value.GetString(), out number)) return number;
                }
                var nested = FindInt32(property.Value, keys);
                if (nested is not null) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                var nested = FindInt32(child, keys);
                if (nested is not null) return nested;
            }
        }
        return null;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) { value = property.Value; return true; }
            }
        }
        value = default;
        return false;
    }

    private static string FirstNonEmpty(params string?[] values) => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    private static bool LooksLikeMissingAlias(string text)
    {
        var value = text.ToLowerInvariant();
        return new[] { "not found", "unknown alias", "no runtime alias", "does not exist", "is not known" }.Any(value.Contains);
    }
    private static bool SamePath(string left, string right)
    {
        try { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private sealed record ProfileEntry(string Name, string Path);
    private sealed record RuntimeEntry(string Alias, string ProfileName, string ProfilePath, string TunnelId);
    private sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);
    private sealed record RuntimeSnapshot(
        string Alias,
        RuntimeState State,
        bool ProcessRunning,
        bool Healthy,
        bool Ready,
        string TunnelId,
        string ProfilePath,
        string HealthUrl,
        string HealthDetailsUrl,
        string McpHealthUrl,
        string LogPath,
        int? ProcessId,
        string ErrorMessage,
        JsonElement Raw)
    {
        public static RuntimeSnapshot Stopped(string alias, string error = "") => new(alias, RuntimeState.Stopped, false, false, false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, null, error, default);
        public static RuntimeSnapshot Failed(string alias, string error) => new(alias, RuntimeState.Error, false, false, false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, null, error, default);
    }
}
