using System.Diagnostics;
using System.Text;
using OpenAITunnelManager.Core.Models;
using OpenAITunnelManager.Infrastructure.Settings;

namespace OpenAITunnelManager.Infrastructure.TunnelClient;

public sealed partial class TunnelClientOperations
{
    public async Task StartAsync(
        TunnelConnection connection,
        string? secret,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        if (connection.HasRuntime)
        {
            await ConnectRuntimeAsync(connection, secret, cancellationToken, environment);
            return;
        }
        if (connection.HasProfile)
        {
            await StartProfileAsync(connection, secret, cancellationToken, environment);
            return;
        }
        throw new InvalidOperationException("该项目没有可启动的 Runtime 或 Profile");
    }

    public async Task StopAsync(TunnelConnection connection, CancellationToken cancellationToken = default)
    {
        if (connection.HasRuntime)
        {
            await RunAsync(["runtimes", "stop", connection.RuntimeAlias, "--json"], false, cancellationToken, TimeSpan.FromSeconds(30));
            return;
        }
        if (connection.HasProfile)
        {
            await StopProfileAsync(connection.ProfileName);
            return;
        }
        throw new InvalidOperationException("该项目没有可停止的 Runtime 或 Profile");
    }

    public async Task RestartAsync(
        TunnelConnection connection,
        string? secret,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        await StopAsync(connection, cancellationToken);
        await StartAsync(connection, secret, cancellationToken, environment);
    }

    public async Task RemoveRuntimeAsync(string alias, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        await RunAsync(["runtimes", "rm", alias, "--json"], false, cancellationToken, TimeSpan.FromSeconds(30));
    }

    private async Task ConnectRuntimeAsync(
        TunnelConnection connection,
        string? secret,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment)
    {
        var profilePath = FirstNonEmpty(connection.RuntimeProfilePath, connection.ProfilePath);
        var metadata = ProfileMetadataReader.Read(profilePath);
        var tunnelId = FirstNonEmpty(connection.TunnelId, metadata.TunnelId);
        if (string.IsNullOrWhiteSpace(tunnelId)) throw new InvalidOperationException("官方 Runtime 状态/Profile 中缺少 Tunnel ID，无法安全重建 connect 命令");
        var profileName = FirstNonEmpty(connection.RuntimeProfileName, connection.ProfileName, connection.RuntimeAlias);
        var kind = FirstNonEmpty(connection.TargetKind, metadata.TargetKind).ToLowerInvariant().Replace('-', '_');
        var target = FirstNonEmpty(connection.TargetValue, metadata.TargetValue);
        if (string.IsNullOrWhiteSpace(target)) throw new InvalidOperationException("官方 Runtime 状态/Profile 中缺少 main MCP target；请用 tunnel-client 修复该 Runtime 配置");

        var args = new List<string>
        {
            "runtimes", "connect", "--alias", connection.RuntimeAlias, "--tunnel-id", tunnelId,
            "--profile", profileName, "--runtime-api-key", metadata.ApiKeyRef
        };
        var profileDirectory = Path.GetDirectoryName(profilePath);
        if (!string.IsNullOrWhiteSpace(profileDirectory)) args.AddRange(["--profile-dir", profileDirectory]);
        if (kind is "server_url" or "mcp_server_url" or "url" or "http" or "https") args.AddRange(["--mcp-server-url", target]);
        else if (kind is "command" or "mcp_command" or "stdio") args.AddRange(["--mcp-command", target]);
        else throw new InvalidOperationException($"不支持的 Runtime MCP target 类型：{connection.TargetKind}");
        args.Add("--json");
        await RunAsync(args, false, cancellationToken, TimeSpan.FromSeconds(90), metadata.ApiKeyRef, secret, environment);
    }

    private async Task StartProfileAsync(
        TunnelConnection connection,
        string? secret,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment)
    {
        var name = connection.ProfileName;
        if (_foreground.TryGetValue(name, out var existing) && !existing.Process.HasExited) return;
        if (existing is not null)
        {
            _foreground.TryRemove(name, out _);
            existing.LogWriter.Dispose();
            existing.Process.Dispose();
        }

        var safeName = string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var workDirectory = Path.Combine(AppDataPaths.Current.ForegroundStateDirectory, safeName);
        Directory.CreateDirectory(workDirectory);
        var healthFile = Path.Combine(workDirectory, "health.url");
        var logPath = Path.Combine(workDirectory, "runtime.log");
        try { File.Delete(healthFile); } catch { }

        var metadata = ProfileMetadataReader.Read(connection.ProfilePath);
        var info = CreateStartInfo(
            ["run", "--profile", name, "--health.listen-addr", "127.0.0.1:0", "--health.url-file", healthFile],
            metadata.ApiKeyRef,
            secret,
            environment);
        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        var logStream = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
        var logWriter = new StreamWriter(logStream, new UTF8Encoding(false)) { AutoFlush = true };
        var gate = new object();
        void Append(string? line)
        {
            if (line is null) return;
            lock (gate)
            {
                try { logWriter.WriteLine(line); }
                catch (ObjectDisposedException) { }
            }
        }
        process.OutputDataReceived += (_, e) => Append(e.Data);
        process.ErrorDataReceived += (_, e) => Append(e.Data);

        try
        {
            if (!process.Start()) throw new InvalidOperationException("无法启动 tunnel-client Profile 进程");
        }
        catch
        {
            logWriter.Dispose();
            process.Dispose();
            throw;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _foreground[name] = new ForegroundProfile(process, healthFile, logPath, connection.ProfilePath, logWriter);

        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(12);
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (process.HasExited) break;
                if (File.Exists(healthFile))
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(await File.ReadAllTextAsync(healthFile, cancellationToken))) break;
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                    }
                }
                await Task.Delay(75, cancellationToken);
            }

            if (process.HasExited) throw new InvalidOperationException($"Profile 前台进程已退出，退出码 {process.ExitCode}。日志：{logPath}");
        }
        catch
        {
            await StopProfileAsync(name);
            throw;
        }

        process.Exited += (_, _) => HandleForegroundProfileExited(name, process);
        try
        {
            if (process.HasExited) HandleForegroundProfileExited(name, process);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void HandleForegroundProfileExited(string name, Process process)
    {
        if (!_foreground.TryGetValue(name, out var current) || !ReferenceEquals(current.Process, process)) return;
        if (!_foreground.TryRemove(name, out var record) || !ReferenceEquals(record.Process, process)) return;

        try { process.CancelOutputRead(); } catch { }
        try { process.CancelErrorRead(); } catch { }
        try { record.LogWriter.Dispose(); } catch { }
        try { process.Dispose(); } catch { }

        try { ForegroundProfileExited?.Invoke(name); } catch { }
    }

    private Task StopProfileAsync(string name)
    {
        if (!_foreground.TryRemove(name, out var record)) return Task.CompletedTask;
        try
        {
            if (!record.Process.HasExited)
            {
                record.Process.Kill(entireProcessTree: true);
                record.Process.WaitForExit(5000);
            }
            try { record.Process.CancelOutputRead(); } catch { }
            try { record.Process.CancelErrorRead(); } catch { }
        }
        finally
        {
            try { record.LogWriter.Dispose(); } catch { }
            record.Process.Dispose();
        }
        return Task.CompletedTask;
    }

    public async Task ShutdownForegroundProfilesAsync()
    {
        foreach (var name in _foreground.Keys.ToArray())
        {
            try { await StopProfileAsync(name); } catch { }
        }
    }
}
