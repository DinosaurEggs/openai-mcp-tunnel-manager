using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace OpenAITunnelManager.Infrastructure.TunnelClient;

public sealed class TunnelClientProcessRunner(TunnelClientOptions options)
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    public ProcessStartInfo CreateStartInfo(
        IEnumerable<string> arguments,
        string? secretRef = null,
        string? secret = null)
    {
        var info = new ProcessStartInfo
        {
            FileName = options.ResolveExecutablePath(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        options.ApplyChildEnvironment(info);
        var environmentName = SecretEnvironmentName(secretRef);
        if (!string.IsNullOrEmpty(secret) && environmentName is not null)
            info.Environment[environmentName] = secret;

        return info;
    }

    public async Task<TunnelClientProcessResult> RunAsync(
        IEnumerable<string> arguments,
        bool allowFailure,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null,
        string? secretRef = null,
        string? secret = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var args = arguments.ToArray();
        var info = CreateStartInfo(args, secretRef, secret);
        using var process = new Process { StartInfo = info };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("无法启动 tunnel-client 进程");
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException($"无法启动 tunnel-client：{info.FileName}", exception);
        }

        process.StandardInput.Close();

        // Do not bind the stream readers to the caller token. If cancellation or timeout
        // happens we first terminate the process tree, then let both readers drain to EOF.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? options.CommandTimeout);

        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await TerminateProcessTreeAsync(process).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            throw new TimeoutException($"tunnel-client 命令执行超时：{string.Join(' ', args)}");
        }

        var result = new TunnelClientProcessResult(
            process.ExitCode,
            Redact(await stdoutTask.ConfigureAwait(false), secret),
            Redact(await stderrTask.ConfigureAwait(false), secret));

        if (!allowFailure && result.ExitCode != 0)
        {
            var detail = FirstNonEmpty(result.StandardError.Trim(), result.StandardOutput.Trim(), "未知错误");
            throw new InvalidOperationException($"tunnel-client 命令失败（退出码 {result.ExitCode}）：{detail}");
        }

        return result;
    }

    private static async Task TerminateProcessTreeAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Best effort: the process may have exited between HasExited and Kill.
        }

        try
        {
            if (!process.HasExited)
            {
                using var cleanupDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await process.WaitForExitAsync(cleanupDeadline.Token).ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException)
        {
            // Process is already gone or was never associated with a handle.
        }
        catch (OperationCanceledException)
        {
            // Cleanup is bounded even when the OS refuses to terminate the process.
        }
    }

    public static string? SecretEnvironmentName(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        var name = reference.StartsWith("env:", StringComparison.OrdinalIgnoreCase) ? reference[4..]
            : reference.StartsWith("${", StringComparison.Ordinal) && reference.EndsWith('}') ? reference[2..^1]
            : reference.StartsWith('(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}

public sealed record TunnelClientProcessResult(int ExitCode, string StandardOutput, string StandardError);
) ? reference[1..] : string.Empty;
        return System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$") ? name : null;
    }

    private static string Redact(string text, string? secret) =>
        string.IsNullOrEmpty(secret) ? text : text.Replace(secret, "[REDACTED]", StringComparison.Ordinal);

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}

public sealed record TunnelClientProcessResult(int ExitCode, string StandardOutput, string StandardError);
