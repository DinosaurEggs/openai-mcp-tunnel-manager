using System.Diagnostics;
using System.Text.Json;
using OpenAITunnelManager.Core.Abstractions;
using OpenAITunnelManager.Core.Models;

namespace OpenAITunnelManager.Infrastructure.TunnelClient;

public sealed class TunnelClientService(TunnelClientOptions options) : ITunnelClientService
{
    public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["--version"], allowFailure: false, cancellationToken).ConfigureAwait(false);
        return result.StandardOutput.Trim();
    }

    public async Task<IReadOnlyList<TunnelConnection>> GetConnectionsAsync(CancellationToken cancellationToken = default)
    {
        using var profilesJson = await RunJsonAsync(["profiles", "list", "--json"], cancellationToken).ConfigureAwait(false);
        using var runtimesJson = await RunJsonAsync(["runtimes", "list", "--json"], cancellationToken).ConfigureAwait(false);

        var profiles = ParseProfiles(profilesJson.RootElement);
        var profilesByName = profiles.ToDictionary(static profile => profile.Name, StringComparer.OrdinalIgnoreCase);
        var linkedProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var connections = new List<TunnelConnection>();

        foreach (var runtime in ParseRuntimes(runtimesJson.RootElement))
        {
            profilesByName.TryGetValue(runtime.ProfileName, out var listedProfile);
            if (listedProfile is not null && !string.IsNullOrWhiteSpace(runtime.ProfilePath) &&
                !SamePath(listedProfile.Path, runtime.ProfilePath))
            {
                listedProfile = null;
            }

            if (listedProfile is not null)
            {
                linkedProfiles.Add(listedProfile.Name);
            }

            var profilePath = listedProfile?.Path ?? runtime.ProfilePath;
            var profileName = !string.IsNullOrWhiteSpace(runtime.ProfileName)
                ? runtime.ProfileName
                : listedProfile?.Name ?? string.Empty;
            var metadata = ProfileMetadataReader.Read(profilePath);
            var status = await ReadStatusAsync(runtime.Alias, cancellationToken).ConfigureAwait(false);
            var target = ResolveTarget(status.Raw, metadata);

            connections.Add(new TunnelConnection(
                Name: runtime.Alias,
                ProfileName: profileName,
                ProfilePath: profilePath,
                RuntimeAlias: runtime.Alias,
                RuntimeProfileName: runtime.ProfileName,
                RuntimeProfilePath: runtime.ProfilePath,
                TunnelId: FirstNonEmpty(status.TunnelId, runtime.TunnelId, metadata.TunnelId),
                TargetKind: target.Kind,
                TargetValue: target.Value,
                State: status.State,
                ProcessRunning: status.ProcessRunning,
                Healthy: status.Healthy,
                Ready: status.Ready,
                HealthUrl: status.HealthUrl,
                LogPath: status.LogPath,
                ProcessId: status.ProcessId,
                Error: status.ErrorMessage));
        }

        foreach (var profile in profiles)
        {
            if (linkedProfiles.Contains(profile.Name))
            {
                continue;
            }

            var metadata = ProfileMetadataReader.Read(profile.Path);
            connections.Add(new TunnelConnection(
                Name: profile.Name,
                ProfileName: profile.Name,
                ProfilePath: profile.Path,
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
                LogPath: string.Empty,
                ProcessId: null,
                Error: string.Empty));
        }

        return connections
            .OrderBy(static connection => connection.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static connection => connection.RuntimeAlias, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<TunnelConnection> GetStatusAsync(
        TunnelConnection connection,
        CancellationToken cancellationToken = default)
    {
        if (!connection.HasRuntime)
        {
            return connection;
        }

        var status = await ReadStatusAsync(connection.RuntimeAlias, cancellationToken).ConfigureAwait(false);
        var metadata = ProfileMetadataReader.Read(connection.ProfilePath);
        var target = ResolveTarget(status.Raw, metadata);

        return connection with
        {
            TunnelId = FirstNonEmpty(status.TunnelId, connection.TunnelId, metadata.TunnelId),
            TargetKind = target.Kind,
            TargetValue = target.Value,
            State = status.State,
            ProcessRunning = status.ProcessRunning,
            Healthy = status.Healthy,
            Ready = status.Ready,
            HealthUrl = status.HealthUrl,
            LogPath = status.LogPath,
            ProcessId = status.ProcessId,
            Error = status.ErrorMessage
        };
    }

    public async Task StopRuntimeAsync(string alias, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        await RunAsync(["runtimes", "stop", alias, "--json"], allowFailure: false, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<RuntimeStatusSnapshot> ReadStatusAsync(string alias, CancellationToken cancellationToken)
    {
        var result = await RunAsync(["runtimes", "status", alias, "--json"], allowFailure: true, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return result.ExitCode == 0
                ? RuntimeStatusSnapshot.Empty(alias)
                : RuntimeStatusSnapshot.Failure(alias, result.StandardError.Trim());
        }

        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return RuntimeStatusSnapshot.Failure(alias, "Runtime 状态 JSON 根节点不是对象");
            }

            var processRunning = GetBoolean(root, "process_running", "running");
            var healthy = GetBoolean(root, "healthy");
            var ready = GetBoolean(root, "ready");
            var runtimeState = GetString(root, "runtime_state", "state").ToLowerInvariant();
            var stale = GetBoolean(root, "stale") || runtimeState is "stale" or "stale_alias";

            var state = stale
                ? RuntimeState.Stale
                : ready && processRunning
                    ? RuntimeState.Ready
                    : processRunning && healthy
                        ? RuntimeState.Running
                        : processRunning
                            ? RuntimeState.Starting
                            : runtimeState is "error" or "failed"
                                ? RuntimeState.Error
                                : runtimeState is "stopped" or "disconnected" or "not_running" or "missing_profile"
                                    ? RuntimeState.Stopped
                                    : result.ExitCode == 0 ? RuntimeState.Stopped : RuntimeState.Error;

            return new RuntimeStatusSnapshot(
                Alias: alias,
                State: state,
                ProcessRunning: processRunning,
                Healthy: healthy,
                Ready: ready,
                TunnelId: GetString(root, "tunnel_id"),
                HealthUrl: FindString(root, "health_url", "health_base_url"),
                LogPath: FindString(root, "log_path", "log_file", "log_file_path", "runtime_log", "logs_path", "logs"),
                ProcessId: FindInt32(root, "pid", "process_id"),
                ErrorMessage: FirstNonEmpty(
                    GetString(root, "error", "remote_error"),
                    result.ExitCode == 0 ? string.Empty : result.StandardError.Trim()),
                Raw: root.Clone());
        }
        catch (JsonException)
        {
            return RuntimeStatusSnapshot.Failure(
                alias,
                FirstNonEmpty(result.StandardError.Trim(), "Runtime 状态不是有效 JSON"));
        }
    }

    private async Task<JsonDocument> RunJsonAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(arguments, allowFailure: false, cancellationToken).ConfigureAwait(false);
        try
        {
            return JsonDocument.Parse(result.StandardOutput);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"tunnel-client 返回了无效 JSON：{string.Join(' ', arguments)}",
                exception);
        }
    }

    private async Task<CommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        bool allowFailure,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = options.ResolveExecutablePath(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("无法启动 tunnel-client 进程");
            }
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException($"无法启动 tunnel-client：{startInfo.FileName}", exception);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.CommandTimeout);

        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw new TimeoutException($"tunnel-client 命令执行超时：{string.Join(' ', arguments)}");
        }

        var result = new CommandResult(
            process.ExitCode,
            await stdoutTask.ConfigureAwait(false),
            await stderrTask.ConfigureAwait(false));

        if (!allowFailure && result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"tunnel-client 命令失败（退出码 {result.ExitCode}）：" +
                FirstNonEmpty(result.StandardError.Trim(), result.StandardOutput.Trim()));
        }

        return result;
    }

    private static IReadOnlyList<ProfileEntry> ParseProfiles(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("profiles list JSON 根节点不是数组");
        }

        var profiles = new List<ProfileEntry>();
        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = GetString(item, "name");
            var path = GetString(item, "path");
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(path))
            {
                profiles.Add(new ProfileEntry(name, path));
            }
        }

        return profiles;
    }

    private static IReadOnlyList<RuntimeEntry> ParseRuntimes(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !TryGetProperty(root, "aliases", out var aliases) ||
            aliases.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var runtimes = new List<RuntimeEntry>();
        foreach (var item in aliases.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var alias = GetString(item, "alias");
            if (string.IsNullOrWhiteSpace(alias))
            {
                continue;
            }

            runtimes.Add(new RuntimeEntry(
                Alias: alias,
                ProfileName: GetString(item, "profile_name"),
                ProfilePath: FirstNonEmpty(GetString(item, "profile_path"), GetString(item, "config_path")),
                TunnelId: GetString(item, "tunnel_id")));
        }

        return runtimes;
    }

    private static (string Kind, string Value) ResolveTarget(JsonElement status, ProfileMetadata metadata)
    {
        if (status.ValueKind == JsonValueKind.Object &&
            TryGetProperty(status, "process", out var process) &&
            process.ValueKind == JsonValueKind.Object)
        {
            var kind = GetString(process, "target_kind");
            var value = GetString(process, "target_value");
            if (!string.IsNullOrWhiteSpace(kind) && !string.IsNullOrWhiteSpace(value))
            {
                return (kind, value);
            }
        }

        return (metadata.TargetKind, metadata.TargetValue);
    }

    private static string GetString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(element, name, out var value))
            {
                continue;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString()?.Trim() ?? string.Empty,
                JsonValueKind.Number => value.GetRawText(),
                _ => string.Empty
            };
        }

        return string.Empty;
    }

    private static bool GetBoolean(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(element, name, out var value))
            {
                continue;
            }

            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return value.GetBoolean();
            }

            if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return false;
    }

    private static string FindString(JsonElement element, params string[] keys)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (keys.Any(key => string.Equals(key, property.Name, StringComparison.OrdinalIgnoreCase)) &&
                    property.Value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(property.Value.GetString()))
                {
                    return property.Value.GetString()!.Trim();
                }

                var nested = FindString(property.Value, keys);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                var nested = FindString(child, keys);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
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
                    if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var number))
                    {
                        return number;
                    }

                    if (property.Value.ValueKind == JsonValueKind.String &&
                        int.TryParse(property.Value.GetString(), out number))
                    {
                        return number;
                    }
                }

                var nested = FindInt32(property.Value, keys);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                var nested = FindInt32(child, keys);
                if (nested is not null)
                {
                    return nested;
                }
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
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static bool SamePath(string left, string right)
    {
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private sealed record ProfileEntry(string Name, string Path);

    private sealed record RuntimeEntry(string Alias, string ProfileName, string ProfilePath, string TunnelId);

    private sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed record RuntimeStatusSnapshot(
        string Alias,
        RuntimeState State,
        bool ProcessRunning,
        bool Healthy,
        bool Ready,
        string TunnelId,
        string HealthUrl,
        string LogPath,
        int? ProcessId,
        string ErrorMessage,
        JsonElement Raw)
    {
        public static RuntimeStatusSnapshot Empty(string alias) => new(
            alias,
            RuntimeState.Unknown,
            false,
            false,
            false,
            string.Empty,
            string.Empty,
            string.Empty,
            null,
            string.Empty,
            default);

        public static RuntimeStatusSnapshot Failure(string alias, string errorMessage) => new(
            alias,
            RuntimeState.Error,
            false,
            false,
            false,
            string.Empty,
            string.Empty,
            string.Empty,
            null,
            errorMessage,
            default);
    }
}
