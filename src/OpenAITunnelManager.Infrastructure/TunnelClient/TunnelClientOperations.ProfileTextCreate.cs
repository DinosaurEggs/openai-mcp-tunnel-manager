using System.Text;
using OpenAITunnelManager.Infrastructure.Settings;

namespace OpenAITunnelManager.Infrastructure.TunnelClient;

public sealed partial class TunnelClientOperations
{
    public async Task CreateProfileTextAsync(string name, string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Profile 名称不能为空", nameof(name));
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Profile 内容不能为空", nameof(text));

        var tempDirectory = Path.Combine(AppDataPaths.Current.StateDirectory, "temp");
        Directory.CreateDirectory(tempDirectory);
        var tempPath = Path.Combine(tempDirectory, $"profile-create-{Guid.NewGuid():N}.yaml");
        try
        {
            await File.WriteAllTextAsync(tempPath, text, new UTF8Encoding(false), cancellationToken);
            await RunAsync(["profiles", "add", name.Trim(), "--from-file", tempPath, "--force"], false, cancellationToken, TimeSpan.FromSeconds(30));
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }
}
