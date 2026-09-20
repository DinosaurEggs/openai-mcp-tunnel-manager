using System.Diagnostics;
using OpenAITunnelManager.Core.Models;
using OpenAITunnelManager.Infrastructure.Settings;

namespace OpenAITunnelManager.Infrastructure.TunnelClient;

public sealed class TunnelClientOptions
{
    public TunnelClientSource Source { get; set; } = TunnelClientSource.Managed;
    public string ExecutablePath { get; set; } = string.Empty;
    public string ProfileDirectoryOverride { get; set; } = string.Empty;
    public string StateDirectoryOverride { get; set; } = string.Empty;
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(45);

    public void Apply(AppSettings settings)
    {
        Source = settings.TunnelClientSource;
        ExecutablePath = Source == TunnelClientSource.Managed
            ? ResolveManagedConfiguredPath(settings.ManagedTunnelClientVersion)
            : settings.TunnelClientPath?.Trim() ?? string.Empty;
        ProfileDirectoryOverride = settings.ProfileDirectoryOverride?.Trim() ?? string.Empty;
        StateDirectoryOverride = settings.StateDirectoryOverride?.Trim() ?? string.Empty;
    }

    public string ResolveExecutablePath()
    {
        var configured = ExecutablePath.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured));
            if (File.Exists(full))
            {
                return full;
            }

            throw new FileNotFoundException(
                $"已配置的 tunnel-client 不存在：{configured}。请在设置中重新配置 tunnel-client。",
                full);
        }

        if (Source == TunnelClientSource.Managed)
        {
            throw new FileNotFoundException(
                "尚未安装由 Manager 管理的 tunnel-client。请下载最新版，或切换到自定义 tunnel-client。");
        }

        throw new FileNotFoundException("未选择自定义 tunnel-client.exe。");
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

    private static string ResolveManagedConfiguredPath(string version)
    {
        var value = version?.Trim() ?? string.Empty;
        return value.Length == 0
            ? string.Empty
            : AppDataPaths.Current.GetManagedTunnelClientExecutablePath(value);
    }
}
