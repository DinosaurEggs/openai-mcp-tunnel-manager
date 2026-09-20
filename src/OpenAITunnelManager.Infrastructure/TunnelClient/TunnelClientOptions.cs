using System.Diagnostics;
using OpenAITunnelManager.Core.Models;

namespace OpenAITunnelManager.Infrastructure.TunnelClient;

public sealed class TunnelClientOptions
{
    public string ExecutablePath { get; set; } = string.Empty;
    public bool UseManagedClient { get; set; } = true;
    public string ProfileDirectoryOverride { get; set; } = string.Empty;
    public string StateDirectoryOverride { get; set; } = string.Empty;
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(45);

    public void Apply(AppSettings settings)
    {
        UseManagedClient = !string.Equals(settings.TunnelClientSource, "custom", StringComparison.OrdinalIgnoreCase);
        ExecutablePath = UseManagedClient
            ? TunnelClientUpdateService.ManagedExecutablePath
            : settings.TunnelClientPath?.Trim() ?? string.Empty;
        ProfileDirectoryOverride = settings.ProfileDirectoryOverride?.Trim() ?? string.Empty;
        StateDirectoryOverride = settings.StateDirectoryOverride?.Trim() ?? string.Empty;
    }

    public string ResolveExecutablePath()
    {
        var configured = ExecutablePath.Trim();
        if (UseManagedClient)
        {
            if (File.Exists(configured)) return Path.GetFullPath(configured);

            throw new FileNotFoundException(
                "未找到托管的 tunnel-client.exe。请在设置中下载最新版，或切换到自定义 tunnel-client。",
                configured);
        }

        if (!string.IsNullOrWhiteSpace(configured))
        {
            var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured));
            if (File.Exists(full)) return full;

            throw new FileNotFoundException(
                $"已配置的 tunnel-client 不存在：{configured}。请在设置中重新选择 tunnel-client.exe。",
                full);
        }

        throw new FileNotFoundException("未找到自定义 tunnel-client。请在设置中重新选择 tunnel-client.exe。");
    }

    public void ApplyChildEnvironment(ProcessStartInfo startInfo)
    {
        if (!string.IsNullOrWhiteSpace(ProfileDirectoryOverride))
        {
            startInfo.Environment["TUNNEL_CLIENT_PROFILE_DIR"] = ProfileDirectoryOverride;
        }

        if (!string.IsNullOrWhiteSpace(StateDirectoryOverride))
        {
            startInfo.Environment["TUNNEL_CLIENT_STATE_DIR"] = StateDirectoryOverride;
        }
    }
}
