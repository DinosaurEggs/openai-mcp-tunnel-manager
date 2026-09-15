using System.Text;
using System.Text.RegularExpressions;
using OpenAITunnelManager.Core.Models;

namespace OpenAITunnelManager.Infrastructure.TunnelClient;

public sealed partial class TunnelClientOperations
{
    private static readonly Regex ProfileNamePattern = new(
        @"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task CreateProfileAsync(ProfileSpec spec, CancellationToken cancellationToken = default)
    {
        var errors = spec.Validate();
        if (errors.Count > 0) throw new ArgumentException(string.Join("；", errors));
        var args = new List<string>
        {
            "init", "--profile", spec.Name.Trim(), "--tunnel-id", spec.TunnelId.Trim(),
            "--control-plane-api-key-ref", ProfileMetadata.DefaultApiKeyRef
        };
        args.AddRange(spec.McpType == McpType.Http
            ? ["--mcp-server-url", spec.McpTarget.Trim()]
            : ["--mcp-command", spec.McpTarget.Trim()]);
        await RunAsync(args, false, cancellationToken, TimeSpan.FromSeconds(30));
    }

    public async Task ImportProfileAsync(string name, string sourcePath, CancellationToken cancellationToken = default)
    {
        name = name.Trim();
        if (!ProfileNamePattern.IsMatch(name)) throw new ArgumentException("导入文件名不是有效的 Profile 名称", nameof(name));
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) throw new FileNotFoundException("要导入的 Profile 文件不存在", sourcePath);
        await RunAsync(["profiles", "add", name, "--from-file", Path.GetFullPath(sourcePath)], false, cancellationToken, TimeSpan.FromSeconds(30));
    }

    public async Task ExportProfileAsync(string name, string expectedPath, string destinationPath, CancellationToken cancellationToken = default)
    {
        var actual = await VerifyProfileEntryAsync(name, expectedPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(destinationPath)) throw new ArgumentException("导出路径不能为空", nameof(destinationPath));
        var destination = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        await using var source = new FileStream(actual, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    public async Task<string> ReadProfileTextAsync(string name, string expectedPath, CancellationToken cancellationToken = default)
    {
        var actual = await VerifyProfileEntryAsync(name, expectedPath, cancellationToken);
        return await File.ReadAllTextAsync(actual, Encoding.UTF8, cancellationToken);
    }

    public async Task SaveProfileTextAsync(string name, string expectedPath, string text, CancellationToken cancellationToken = default)
    {
        _ = await VerifyProfileEntryAsync(name, expectedPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Profile 内容不能为空", nameof(text));
        var tempDirectory = Path.Combine(AppContext.BaseDirectory, "state", "temp");
        Directory.CreateDirectory(tempDirectory);
        var tempPath = Path.Combine(tempDirectory, $"profile-{Guid.NewGuid():N}.yaml");
        try
        {
            await File.WriteAllTextAsync(tempPath, text, new UTF8Encoding(false), cancellationToken);
            await RunAsync(["profiles", "add", name, "--from-file", tempPath, "--force"], false, cancellationToken, TimeSpan.FromSeconds(30));
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    public async Task DeleteProfileAsync(string name, string expectedPath, CancellationToken cancellationToken = default)
    {
        var actual = await VerifyProfileEntryAsync(name, expectedPath, cancellationToken);

        // A profile-only connection can be running as a Manager-owned foreground
        // tunnel-client process. Stop that process before removing the official
        // Profile entry so deletion cannot leave an orphan process behind.
        if (_foreground.ContainsKey(name)) await StopProfileAsync(name);

        try { File.Delete(actual); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"删除 Profile 失败：{exception.Message}", exception);
        }
    }
}
