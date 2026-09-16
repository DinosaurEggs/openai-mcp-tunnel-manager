using OpenAITunnelManager.Core.Models;
using OpenAITunnelManager.Infrastructure.TunnelClient;

namespace OpenAITunnelManager.App.ViewModels;

public sealed partial class ConnectionsViewModel
{
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

        var updated = ProfileDocumentEditor.ApplyCommonFields(
            rawText,
            metadata.TunnelId,
            metadata.TargetKind,
            metadata.TargetValue,
            spec.TunnelId.Trim(),
            spec.McpTarget.Trim());

        IsBusy = true;
        StatusMessage = $"正在创建 Profile：{spec.Name}";
        try
        {
            await _operations.CreateProfileTextAsync(spec.Name.Trim(), updated);
            Settings.ProfilePreferences[$"profile:{spec.Name.Trim()}"] = preference;
            if (!string.IsNullOrWhiteSpace(secret)) _credentials.Set(spec.Name.Trim(), secret);
            await _settingsStore.SaveAsync(Settings);
            await RefreshAfterOperationAsync($"profile:{spec.Name.Trim()}", $"Profile 已创建：{spec.Name.Trim()}");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
