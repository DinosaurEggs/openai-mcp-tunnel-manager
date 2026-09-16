using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using OpenAITunnelManager.Core.Abstractions;
using OpenAITunnelManager.Core.Models;

namespace OpenAITunnelManager.Infrastructure.TunnelClient;

public sealed partial class TunnelClientOperations : ITunnelClientOperations
{
    private readonly TunnelClientOptions _options;
    private readonly ITunnelClientService _inventory;
    private readonly TunnelClientProcessRunner _runner;
    private readonly ConcurrentDictionary<string, ForegroundProfile> _foreground = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _capabilitiesGate = new(1, 1);
    private CapabilityCache? _capabilityCache;

    public TunnelClientOperations(TunnelClientOptions options, ITunnelClientService inventory)
    {
        _options = options;
        _inventory = inventory;
        _runner = new TunnelClientProcessRunner(options);
    }

    public event Action<string>? ForegroundProfileExited;

    public string ResolveExecutablePath() => _options.ResolveExecutablePath();

    public async Task<TunnelClientCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        var executable = ResolveExecutablePath();
        var lastWrite = File.GetLastWriteTimeUtc(executable);
        var cached = _capabilityCache;
        if (cached is not null &&
            string.Equals(cached.ExecutablePath, executable, StringComparison.OrdinalIgnoreCase) &&
            cached.LastWriteTimeUtc == lastWrite)
        {
            return cached.Capabilities;
        }

        await _capabilitiesGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = _capabilityCache;
            if (cached is not null &&
                string.Equals(cached.ExecutablePath, executable, StringComparison.OrdinalIgnoreCase) &&
                cached.LastWriteTimeUtc == lastWrite)
            {
                return cached.Capabilities;
            }

            var version = (await RunAsync(["--version"], false, cancellationToken, TimeSpan.FromSeconds(10))).StandardOutput.Trim();
            async Task<bool> Supports(params string[] args) =>
                (await RunAsync(args, true, cancellationToken, TimeSpan.FromSeconds(10))).ExitCode == 0;

            var profiles = Supports("profiles", "--help");
            var runtimes = Supports("runtimes", "--help");
            var doctor = Supports("doctor", "--help");
            await Task.WhenAll(profiles, runtimes, doctor).ConfigureAwait(false);

            var capabilities = new TunnelClientCapabilities(version, profiles.Result, runtimes.Result, doctor.Result);
            _capabilityCache = new CapabilityCache(executable, lastWrite, capabilities);
            return capabilities;
        }
        finally
        {
            _capabilitiesGate.Release();
        }
    }

    private ProcessStartInfo CreateStartInfo(IEnumerable<string> arguments, string? secretRef = null, string? secret = null) =>
        _runner.CreateStartInfo(arguments, secretRef, secret);

    private Task<TunnelClientProcessResult> RunAsync(
        IEnumerable<string> arguments,
        bool allowFailure,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null,
        string? secretRef = null,
        string? secret = null) =>
        _runner.RunAsync(arguments, allowFailure, cancellationToken, timeout, secretRef, secret);

    private async Task<JsonDocument> RunJsonAsync(IEnumerable<string> args, CancellationToken cancellationToken)
    {
        var result = await RunAsync(args, false, cancellationToken);
        try { return JsonDocument.Parse(result.StandardOutput); }
        catch (JsonException exception) { throw new InvalidOperationException("tunnel-client 返回的内容不是有效 JSON", exception); }
    }

    private async Task<string> VerifyProfileEntryAsync(string name, string expectedPath, CancellationToken cancellationToken)
    {
        using var document = await RunJsonAsync(["profiles", "list", "--json"], cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("profiles list JSON 根节点不是数组");
        var matches = document.RootElement.EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new { Name = GetString(item, "name"), Path = GetString(item, "path") })
            .Where(item => string.Equals(item.Name, name, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1) throw new InvalidOperationException($"无法确认 tunnel-client Profile {name} 的唯一文件，请刷新列表");
        if (!SamePath(matches[0].Path, expectedPath)) throw new InvalidOperationException("Profile 路径已发生变化，请刷新列表");
        return matches[0].Path;
    }

    private static string GetString(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) return string.Empty;
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Any(name => string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase))) continue;
            if (property.Value.ValueKind == JsonValueKind.String) return property.Value.GetString()?.Trim() ?? string.Empty;
            if (property.Value.ValueKind == JsonValueKind.Number) return property.Value.GetRawText();
        }
        return string.Empty;
    }

    private static bool SamePath(string left, string right)
    {
        try { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private sealed record CapabilityCache(string ExecutablePath, DateTime LastWriteTimeUtc, TunnelClientCapabilities Capabilities);
    private sealed record ForegroundProfile(Process Process, string HealthFile, string LogPath, string ProfilePath, StreamWriter LogWriter);
}
