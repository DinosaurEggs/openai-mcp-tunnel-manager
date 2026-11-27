using OpenAITunnelManager.Core.Models;
using OpenAITunnelManager.Infrastructure.TunnelClient;

namespace OpenAITunnelManager.App.ViewModels;

public sealed partial class ConnectionsViewModel
{
    public async Task<IReadOnlyList<string>> GetProfileSamplesAsync()
    {
        EnsureClientAvailable();
        var runner = new TunnelClientProcessRunner(_options);
        var result = await runner.RunAsync(["profiles", "samples", "list"], false, CancellationToken.None);
        return result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('\t')[0]).Where(name => name.StartsWith("sample_", StringComparison.Ordinal)).ToArray();
    }

    public async Task<string> ReadProfileSampleAsync(string name)
    {
        EnsureClientAvailable();
        var runner = new TunnelClientProcessRunner(_options);
        var result = await runner.RunAsync(["profiles", "samples", "show", name], false, CancellationToken.None);
        var output = result.StandardOutput.Replace("\r\n", "\n");
        var start = output.IndexOf("\n\n", StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException("官方模板输出缺少 YAML");
        var raw = output[(start + 2)..];
        _ = ProfileDocumentEditor.ReadMetadata(raw);
        return raw;
    }

    public async Task CreateProfileFromTextAsync(
        string name,
        string rawText,
        string? secret,
        ProfilePreference preference)
    {
        EnsureClientAvailable();
        name = name.Trim();

        IsBusy = true;
        StatusMessage = $"正在创建 Profile：{name}";
        try
        {
            await _operations.CreateProfileTextAsync(name, rawText);
            await SaveCreatedProfilePreferencesAsync(name, preference, secret);
            await RefreshAfterOperationAsync($"profile:{name}", $"Profile 已创建：{name}");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
