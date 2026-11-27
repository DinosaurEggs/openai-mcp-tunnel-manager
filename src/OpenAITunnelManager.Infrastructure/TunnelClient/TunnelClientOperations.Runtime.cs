using System.Diagnostics;
using System.Text;
using OpenAITunnelManager.Core.Models;
using OpenAITunnelManager.Infrastructure.Settings;

namespace OpenAITunnelManager.Infrastructure.TunnelClient;

public sealed partial class TunnelClientOperations
{
    public async Task StartAsync(TunnelConnection connection, string? secret, CancellationToken cancellationToken = default)
    {
        if (connection.HasRuntime)
        {
            await ConnectRuntimeAsync(connection, secret, cancellationToken);
            return;
        }
        if (connection.HasProfile)
        {
            await StartProfileAsync(connection, secret, cancellationToken);
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

    public async Task RestartAsync(TunnelConnection connection, string? secret, CancellationToken cancellationToken = default)
    {
        if (connection.HasRuntime) _ = await ValidateNativeRuntimeAsync(connection, cancellationToken);
        await StopAsync(connection, cancellationToken);
        await StartAsync(connection, secret, cancellationToken);
    }

    public async Task RemoveRuntimeAsync(string alias, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        var inventory = await _inventory.GetConnectionsAsync(cancellationToken);
        var current = inventory.SingleOrDefault(item => item.HasRuntime &&
            string.Equals(item.RuntimeAlias, alias, StringComparison.OrdinalIgnoreCase));
        if (current is null) throw new InvalidOperationException("Runtime 已变化，请刷新列表");
        var path = FirstNonEmpty(current.RuntimeProfilePath, current.ProfilePath);
        if (!string.IsNullOrWhiteSpace(path) && inventory.Any(item =>
            !string.Equals(item.Identity, current.Identity, StringComparison.OrdinalIgnoreCase) &&
            SamePath(path, FirstNonEmpty(item.RuntimeProfilePath, item.ProfilePath))))
            throw new InvalidOperationException("该 Runtime 的 Profile 被其他连接共享，无法安全删除");
        await RunAsync(["runtimes", "rm", alias, "--json"], false, cancellationToken, TimeSpan.FromSeconds(30));
    }

    private async Task<IReadOnlyList<string>> ValidateNativeRuntimeAsync(TunnelConnection connection, CancellationToken cancellationToken)
    {
        var profilePath = FirstNonEmpty(connection.RuntimeProfilePath, connection.ProfilePath);
        if (string.IsNullOrWhiteSpace(profilePath) || !File.Exists(profilePath)) return [];
        var profileName = FirstNonEmpty(connection.RuntimeProfileName, connection.ProfileName, connection.RuntimeAlias);
        var directory = Path.GetDirectoryName(profilePath);
        if (string.IsNullOrWhiteSpace(directory) || !SamePath(profilePath, Path.Combine(directory, profileName + ".yaml")))
            throw new InvalidOperationException("Runtime 的 Profile 路径与官方生成路径不同，请使用完整 Profile 运行方式");
        using var inventory = await RunJsonAsync(["runtimes", "list", "--json"], cancellationToken);
        var stateRoot = GetString(inventory.RootElement, "state_root");
        return NativeRuntimeProfileGuard.Validate(await File.ReadAllTextAsync(profilePath, cancellationToken),
            stateRoot, connection.RuntimeAlias);
    }

    private async Task ConnectRuntimeAsync(TunnelConnection connection, string? secret, CancellationToken cancellationToken)
    {
        var profilePath = FirstNonEmpty(connection.RuntimeProfilePath, connection.ProfilePath);
        var controlPlaneFlags = await ValidateNativeRuntimeAsync(connection, cancellationToken);
        var metadata = ProfileMetadataReader.Read(profilePath);
        var tunnelId = FirstNonEmpty(metadata.TunnelId, connection.TunnelId);
        if (string.IsNullOrWhiteSpace(tunnelId)) throw new InvalidOperationException("官方 Runtime 状态/Profile 中缺少 Tunnel ID，无法安全重建 connect 命令");
        var profileName = FirstNonEmpty(connection.RuntimeProfileName, connection.ProfileName, connection.RuntimeAlias);
        var kind = FirstNonEmpty(metadata.TargetKind, connection.TargetKind).ToLowerInvariant().Replace('-', '_');
        var target = FirstNonEmpty(metadata.TargetValue, connection.TargetValue);
        if (string.IsNullOrWhiteSpace(target)) throw new InvalidOperationException("官方 Runtime 状态/Profile 中缺少 main MCP target；请用 tunnel-client 修复该 Runtime 配置");

        var args = new List<string>
        {
            "runtimes", "connect", "--alias", connection.RuntimeAlias, "--tunnel-id", tunnelId,
            "--profile", profileName, "--runtime-api-key", metadata.ApiKeyRef
        };
        args.AddRange(controlPlaneFlags);
        var profileDirectory = Path.GetDirectoryName(profilePath);
        if (!string.IsNullOrWhiteSpace(profileDirectory)) args.AddRange(["--profile-dir", profileDirectory]);
        if (kind is "server_url" or "mcp_server_url" or "url" or "http" or "https") args.AddRange(["--mcp-server-url", target]);
        else if (kind is "command" or "mcp_command" or "stdio") args.AddRange(["--mcp-command", target]);
        else throw new InvalidOperationException($"不支持的 Runtime MCP target 类型：{connection.TargetKind}");
        args.Add("--json");
        await RunAsync(args, false, cancellationToken, TimeSpan.FromSeconds(90), metadata.ApiKeyRef, secret);
    }

    private async Task StartProfileAsync(TunnelConnection connection, string? secret, CancellationToken cancellationToken)
    {
        await _foregroundGate.WaitAsync(cancellationToken);
        try { await StartProfileCoreAsync(connection, secret, cancellationToken); }
        finally { _foregroundGate.Release(); }
    }

    private async Task StartProfileCoreAsync(TunnelConnection connection, string? secret, CancellationToken cancellationToken)
    {
        var name = connection.ProfileName;
        if (_foreground.TryGetValue(name, out var existing))
        {
            lock (existing)
            {
                if (_foreground.TryGetValue(name, out var current) && ReferenceEquals(current, existing))
                {
                    if (!existing.Process.HasExited)
                    {
                        if (!SamePath(existing.ProfilePath, connection.ProfilePath))
                            throw new InvalidOperationException("同名 Profile 正在使用其他目录中的配置");
                        return;
                    }
                    RemoveForegroundRecord(name, existing);
                }
            }
        }

        var safeName = string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var workDirectory = Path.Combine(AppDataPaths.Current.ForegroundStateDirectory, safeName);
        Directory.CreateDirectory(workDirectory);
        var healthFile = Path.Combine(workDirectory, "health.url");
        var logPath = Path.Combine(workDirectory, "runtime.log");
        try { File.Delete(healthFile); } catch { }

        var metadata = ProfileMetadataReader.Read(connection.ProfilePath);
        var info = CreateStartInfo(
            ["run", "--profile-file", connection.ProfilePath, "--health.listen-addr", "127.0.0.1:0", "--health.url-file", healthFile],
            metadata.ApiKeyRef,
            secret);
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
            await StopProfileCoreAsync(name);
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
        if (!_foreground.TryGetValue(name, out var record) || !ReferenceEquals(record.Process, process)) return;
        lock (record)
        {
            if (record.Stopping || !RemoveForegroundRecord(name, record)) return;
        }
        try { ForegroundProfileExited?.Invoke(name); } catch { }
    }

    // Callers hold the record lock. Only remove the exact process instance.
    private bool RemoveForegroundRecord(string name, ForegroundProfile record)
    {
        if (!_foreground.TryRemove(new KeyValuePair<string, ForegroundProfile>(name, record))) return false;
        try { record.Process.CancelOutputRead(); } catch { }
        try { record.Process.CancelErrorRead(); } catch { }
        try { record.LogWriter.Dispose(); } catch { }
        record.Process.Dispose();
        return true;
    }

    private async Task StopProfileAsync(string name)
    {
        await _foregroundGate.WaitAsync();
        try { await StopProfileCoreAsync(name); }
        finally { _foregroundGate.Release(); }
    }

    private async Task StopProfileCoreAsync(string name)
    {
        if (!_foreground.TryGetValue(name, out var record)) return;
        lock (record)
        {
            if (!_foreground.TryGetValue(name, out var current) || !ReferenceEquals(current, record)) return;
            record.Stopping = true;
        }
        try
        {
            if (!record.Process.HasExited)
            {
                record.Process.Kill(entireProcessTree: true);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await record.Process.WaitForExitAsync(deadline.Token);
            }
            lock (record) { RemoveForegroundRecord(name, record); }
        }
        catch
        {
            lock (record)
            {
                record.Stopping = false;
                if (record.Process.HasExited) RemoveForegroundRecord(name, record);
            }
            throw;
        }
    }

    public async Task ShutdownForegroundProfilesAsync()
    {
        foreach (var name in _foreground.Keys.ToArray())
        {
            try { await StopProfileAsync(name); } catch { }
        }
    }
}
