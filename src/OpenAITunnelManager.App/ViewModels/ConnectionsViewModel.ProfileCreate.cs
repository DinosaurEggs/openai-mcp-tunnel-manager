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
        ProfileSpec spec,
        string rawText,
        string? secret,
        ProfilePreference preference)
    {
        EnsureClientAvailable();
        var errors = spec.Validate();
        if (errors.Count > 0) throw new ArgumentException(string.Join("；", errors));
        if (string.IsNullOrWhiteSpace(rawText)) throw new ArgumentException("Profile 内容不能为空", nameof(rawText));

        var metadata = ProfileDocumentEditor.ReadMetadata(rawText);
        var expectedTargetKind = spec.McpType == McpType.Stdio ? "command" : "server_url";
        if (!string.Equals(metadata.TargetKind, expectedTargetKind, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"高级配置中的 main MCP 类型为 {metadata.TargetKind}，与基本页选择的 {expectedTargetKind} 不一致。请保持一致后再保存。");
        }

        if (metadata.TunnelId != spec.TunnelId || metadata.TargetValue != spec.McpTarget)
            throw new InvalidOperationException("Profile 内容与创建参数不一致");

        IsBusy = true;
        StatusMessage = $"正在创建 Profile：{spec.Name}";
        try
        {
            await _operations.CreateProfileTextAsync(spec.Name.Trim(), rawText);
            await SaveCreatedProfilePreferencesAsync(spec.Name.Trim(), preference, secret);
            await RefreshAfterOperationAsync($"profile:{spec.Name.Trim()}", $"Profile 已创建：{spec.Name.Trim()}");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
