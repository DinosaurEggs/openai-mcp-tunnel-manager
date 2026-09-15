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
    private readonly ConcurrentDictionary<string, ForegroundProfile> _foreground = new(StringComparer.OrdinalIgnoreCase);

    public TunnelClientOperations(TunnelClientOptions options, ITunnelClientService inventory)
    {
        _options = options;
        _inventory = inventory;
    }

    public string ResolveExecutablePath() => _options.ResolveExecutablePath();

    public async Task<TunnelClientCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        var version = (await RunAsync(["--version"], false, cancellationToken, TimeSpan.FromSeconds(10))).Stdout.Trim();
        async Task<bool> Supports(params string[] args) =>
            (await RunAsync(args, true, cancellationToken, TimeSpan.FromSeconds(10))).ExitCode == 0;

        var profiles = Supports("profiles", "--help");
        var runtimes = Supports("runtimes", "--help");
        var doctor = Supports("doctor", "--help");
        await Task.WhenAll(profiles, runtimes, doctor);
        return new TunnelClientCapabilities(version, profiles.Result, runtimes.Result, doctor.Result);
    }

    private ProcessStartInfo CreateStartInfo(IEnumerable<string> arguments, string? secretRef = null, string? secret = null)
    {
        var info = new ProcessStartInfo
        {
            FileName = _options.ResolveExecutablePath(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        _options.ApplyChildEnvironment(info);
        if (!string.IsNullOrWhiteSpace(secret) && !string.IsNullOrWhiteSpace(secretRef) &&
            secretRef.StartsWith("env:", StringComparison.OrdinalIgnoreCase) && secretRef.Length > 4)
        {
            info.Environment[secretRef[4..]] = secret;
        }
        return info;
    }

    private async Task<ProcessResult> RunAsync(
        IEnumerable<string> arguments,
        bool allowFailure,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null,
        string? secretRef = null,
        string? secret = null)
    {
        var args = arguments.ToArray();
        var info = CreateStartInfo(args, secretRef, secret);
        using var process = new Process { StartInfo = info };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("无法启动 tunnel-client 进程");
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException($"无法启动 tunnel-client：{info.FileName}", exception);
        }

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? _options.CommandTimeout);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"tunnel-client 命令执行超时：{string.Join(' ', args)}");
        }

        var result = new ProcessResult(process.ExitCode, await stdout, await stderr);
        if (!allowFailure && result.ExitCode != 0)
        {
            var detail = FirstNonEmpty(result.Stderr.Trim(), result.Stdout.Trim(), "未知错误");
            throw new InvalidOperationException($"tunnel-client 命令失败（退出码 {result.ExitCode}）：{detail}");
        }
        return result;
    }

    private async Task<JsonDocument> RunJsonAsync(IEnumerable<string> args, CancellationToken cancellationToken)
    {
        var result = await RunAsync(args, false, cancellationToken);
        try { return JsonDocument.Parse(result.Stdout); }
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

    private sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);
    private sealed record ForegroundProfile(Process Process, string HealthFile, string LogPath, string ProfilePath);
}
