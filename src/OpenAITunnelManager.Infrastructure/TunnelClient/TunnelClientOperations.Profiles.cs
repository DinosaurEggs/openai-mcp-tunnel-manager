using System.Text;
using OpenAITunnelManager.Core.Models;
using OpenAITunnelManager.Infrastructure.Settings;

namespace OpenAITunnelManager.Infrastructure.TunnelClient;

public sealed partial class TunnelClientOperations
{
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

    public async Task<string> ReadProfileTextAsync(string name, string expectedPath, CancellationToken cancellationToken = default)
    {
        var actual = await VerifyProfileEntryAsync(name, expectedPath, cancellationToken);
        return await File.ReadAllTextAsync(actual, Encoding.UTF8, cancellationToken);
    }

    public async Task SaveProfileTextAsync(string name, string expectedPath, string text, CancellationToken cancellationToken = default)
    {
        _ = await VerifyProfileEntryAsync(name, expectedPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Profile 内容不能为空", nameof(text));
        var tempDirectory = Path.Combine(AppDataPaths.Current.StateDirectory, "temp");
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

        if (_foreground.ContainsKey(name)) await StopProfileAsync(name);

        try { File.Delete(actual); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"删除 Profile 失败：{exception.Message}", exception);
        }
    }
}
