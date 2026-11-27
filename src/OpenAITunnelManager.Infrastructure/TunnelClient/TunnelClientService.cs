using System.Text.Json;
using OpenAITunnelManager.Core.Abstractions;
using OpenAITunnelManager.Core.Models;

namespace OpenAITunnelManager.Infrastructure.TunnelClient;

public sealed class TunnelClientService(TunnelClientProcessRunner runner) : ITunnelClientService
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
        var stateRoot = GetString(runtimesJson.RootElement, "state_root");
        if (string.IsNullOrWhiteSpace(stateRoot))
            throw new InvalidOperationException("runtimes list JSON 缺少有效的 state_root");
        stateRoot = Path.GetFullPath(stateRoot, AppContext.BaseDirectory);
        var profiles = ParseProfiles(profilesJson.RootElement);
        var profilesByName = profiles.ToDictionary(static p => p.Name, StringComparer.OrdinalIgnoreCase);
        var linkedProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<TunnelConnection>();

        // Inventory and status are separate explicit CLI queries.
        foreach (var runtime in ParseRuntimes(runtimesJson.RootElement))
        {
            profilesByName.TryGetValue(runtime.ProfileName, out var listed);
            if (listed is not null && !string.IsNullOrWhiteSpace(runtime.ProfilePath) && !SamePath(listed.Path, runtime.ProfilePath))
            {
                listed = null;
            }
            if (listed is not null) linkedProfiles.Add(listed.Name);

            var profilePath = listed?.Path ?? runtime.ProfilePath;
            var profileName = !string.IsNullOrWhiteSpace(runtime.ProfileName) ? runtime.ProfileName : listed?.Name ?? string.Empty;
            var metadata = ProfileMetadataReader.Read(profilePath);

            result.Add(new TunnelConnection(
                Name: runtime.Alias,
                ProfileName: profileName,
                ProfilePath: FirstNonEmpty(listed?.Path, runtime.ProfilePath),
                ProfileListed: listed is not null,
                RuntimeAlias: runtime.Alias,
                RuntimeProfileName: runtime.ProfileName,
                RuntimeProfilePath: runtime.ProfilePath,
                TunnelId: FirstNonEmpty(runtime.TunnelId, metadata.TunnelId),
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
                Error: string.Empty) { StateRoot = stateRoot, Residency = metadata.Residency, Authentication = metadata.Authentication, RuntimeOwnedPaths = runtime.OwnedPaths, HasStdioTarget = metadata.HasStdioTarget });
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
                Error: string.Empty) { StateRoot = stateRoot, Residency = metadata.Residency, Authentication = metadata.Authentication, HasStdioTarget = metadata.HasStdioTarget });
        }

        return result.OrderBy(static x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static x => x.RuntimeAlias, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static x => x.ProfileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<TunnelConnection> GetStatusAsync(TunnelConnection connection, CancellationToken cancellationToken = default, string? secret = null)
    {
        if (!connection.HasRuntime) return connection;
        var auth = ProfileMetadataReader.Read(FirstNonEmpty(connection.RuntimeProfilePath, connection.ProfilePath));
        var status = await ReadStatusAsync(connection.RuntimeAlias, auth.ApiKeyRef, secret, cancellationToken).ConfigureAwait(false);
        var profilePath = FirstNonEmpty(connection.ProfilePath, connection.RuntimeProfilePath, status.ProfilePath);
        var metadata = ProfileMetadataReader.Read(profilePath);
        var target = ResolveTarget(status.Raw);
        var poll = Child(status.Raw, "control_plane_poll_health");
        var remote = Child(status.Raw, "remote");
        var process = Child(status.Raw, "process");
        var repair = Child(status.Raw, "repair_actions");
        return connection with
        {
            ProfilePath = FirstNonEmpty(connection.ProfilePath, status.ProfilePath),
            RuntimeProfilePath = FirstNonEmpty(status.ProfilePath, connection.RuntimeProfilePath),
            TunnelId = FirstNonEmpty(status.TunnelId, connection.TunnelId, metadata.TunnelId),
            TargetKind = FirstNonEmpty(metadata.TargetKind, target.Kind),
            TargetValue = FirstNonEmpty(metadata.TargetValue, target.Value),
            RunningTargetKind = status.ProcessRunning ? target.Kind : string.Empty,
            RunningTargetValue = status.ProcessRunning ? target.Value : string.Empty,
            RuntimeOwnedPaths = connection.RuntimeOwnedPaths.Concat(OwnedPaths(status.Raw)).Concat(OwnedPaths(process)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            HasStdioTarget = metadata.HasStdioTarget || target.Kind is "command" or "mcp_command" or "stdio",
            ControlPlaneState = GetString(poll, "state"),
            ControlPlaneReason = GetString(poll, "reason"),
            AdminUiUrl = FirstNonEmpty(GetString(status.Raw, "ui_url"), GetString(Child(Child(status.Raw, "local"), "effective_health"), "ui")),
            RemoteResidency = GetString(remote, "residency"),
            RemoteLookupStatus = GetBoolean(status.Raw, "remote_lookup_attempted") ? FirstNonEmpty(GetString(status.Raw, "remote_error"), "已查询") : GetString(status.Raw, "remote_skipped_reason"),
            RepairActions = repair.ValueKind == JsonValueKind.Array ? repair.EnumerateArray().Where(action => action.ValueKind == JsonValueKind.Object).Select(action => new RuntimeRepairAction(GetString(action, "id"), GetString(action, "reason"), GetString(action, "command"))).ToArray() : [],
            StartedAt = GetString(process, "started_at"),
            Residency = metadata.Residency,
            Authentication = metadata.Authentication,
            RefreshedAt = DateTimeOffset.UtcNow,
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

    private async Task<RuntimeSnapshot> ReadStatusAsync(string alias, string? secretRef, string? secret, CancellationToken cancellationToken)
    {
        var command = await runner.RunAsync(["runtimes", "status", alias, "--json"], true, cancellationToken, secretRef: secretRef, secret: secret).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(command.StandardOutput))
        {
            var error = FirstNonEmpty(command.StandardError.Trim(), $"Runtime {alias} 状态为空");
            return LooksLikeMissingAlias(error)
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
                : RuntimeState.Unknown;
            var errorMessage = FirstNonEmpty(GetString(root, "error", "remote_error"), command.ExitCode == 0 ? string.Empty : command.StandardError.Trim());
            if (!stale && !processRunning && command.ExitCode != 0 && LooksLikeMissingAlias(errorMessage)) state = RuntimeState.Stopped;
            return new RuntimeSnapshot(
                alias,
                state,
                processRunning,
                healthy,
                ready,
                GetString(root, "tunnel_id"),
                ResolveReportedPath(FirstNonEmpty(GetString(root, "profile_path", "profile_file"), GetString(Child(root, "process"), "profile_path"), GetString(root, "config_path"))),
                FirstNonEmpty(GetString(root, "health_url", "health_base_url"), GetString(Child(Child(root, "local"), "effective_health"), "url")),
                FirstNonEmpty(GetString(root, "health_details_url"), GetString(Child(Child(root, "local"), "effective_health"), "health_details_url")),
                FirstNonEmpty(GetString(root, "mcp_health_url"), GetString(Child(Child(root, "local"), "effective_health"), "mcp_health_url")),
                ResolveReportedPath(FirstNonEmpty(GetString(Child(root, "process"), "log_path"), GetString(root, "log_path", "log_file"), GetString(Child(Child(root, "local"), "log"), "path"))),
                FindInt32(Child(root, "process"), "pid", "process_id") ?? FindInt32(root, "pid", "process_id"),
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

    private Task<TunnelClientProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        bool allowFailure,
        CancellationToken cancellationToken) =>
        runner.RunAsync(arguments, allowFailure, cancellationToken);

    private static IReadOnlyList<ProfileEntry> ParseProfiles(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("profiles list JSON 根节点不是数组");
        return root.EnumerateArray().Where(static item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new ProfileEntry(GetString(item, "name"), ResolveReportedPath(GetString(item, "path"))))
            .Where(static item => !string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(item.Path)).ToArray();
    }

    private static IReadOnlyList<RuntimeEntry> ParseRuntimes(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !TryGetProperty(root, "aliases", out var aliases) || aliases.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("runtimes list JSON 缺少 aliases 数组");
        return aliases.EnumerateArray().Where(static item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new RuntimeEntry(
                GetString(item, "alias"),
                GetString(item, "profile_name"),
                ResolveReportedPath(FirstNonEmpty(GetString(item, "profile_path"), GetString(item, "config_path"))),
                GetString(item, "tunnel_id"), OwnedPaths(item)))
            .Where(static item => !string.IsNullOrWhiteSpace(item.Alias)).ToArray();
    }

    private static (string Kind, string Value) ResolveTarget(JsonElement status)
    {
        if (status.ValueKind == JsonValueKind.Object && TryGetProperty(status, "process", out var process) && process.ValueKind == JsonValueKind.Object)
        {
            var kind = GetString(process, "target_kind");
            var value = GetString(process, "target_value");
            if (!string.IsNullOrWhiteSpace(kind) && !string.IsNullOrWhiteSpace(value)) return (kind, value);
        }
        return (string.Empty, string.Empty);
    }

    private static IReadOnlyList<string> OwnedPaths(JsonElement element) =>
        new[] { "config_path", "profile_path", "health_url_file", "log_path" }
            .Select(key => ResolveReportedPath(GetString(element, key))).Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();

    private static string ResolveReportedPath(string path) => string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path, AppContext.BaseDirectory);

    private static JsonElement Child(JsonElement element, string name) => TryGetProperty(element, name, out var value) ? value : default;

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

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static bool LooksLikeMissingAlias(string text)
    {
        var value = text.ToLowerInvariant();
        return value.Contains("unknown alias", StringComparison.Ordinal) ||
            value.Contains("no runtime alias", StringComparison.Ordinal) ||
            (value.Contains("alias", StringComparison.Ordinal) &&
                (value.Contains("not found", StringComparison.Ordinal) || value.Contains("does not exist", StringComparison.Ordinal) || value.Contains("is not known", StringComparison.Ordinal)));
    }

    private static bool SamePath(string left, string right)
    {
        try { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private sealed record ProfileEntry(string Name, string Path);
    private sealed record RuntimeEntry(string Alias, string ProfileName, string ProfilePath, string TunnelId, IReadOnlyList<string> OwnedPaths);
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
        public static RuntimeSnapshot Failed(string alias, string error) => new(alias, RuntimeState.Unknown, false, false, false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, null, error, default);
    }
}
