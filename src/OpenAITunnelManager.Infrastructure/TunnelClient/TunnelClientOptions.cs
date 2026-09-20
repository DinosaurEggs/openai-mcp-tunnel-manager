using System.Diagnostics;
using OpenAITunnelManager.Core.Models;

namespace OpenAITunnelManager.Infrastructure.TunnelClient;

public sealed class TunnelClientOptions
{
    public string ExecutablePath { get; set; } = string.Empty;\n    public bool UseManagedClient { get; set; } = true;
    public string ProfileDirectoryOverride { get; set; } = string.Empty;
    public string StateDirectoryOverride { get; set; } = string.Empty;
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(45);

    public void Apply(AppSettings settings)
    {
        UseManagedClient = !string.Equals(settings.TunnelClientSource, "custom", StringComparison.OrdinalIgnoreCase);\n        ExecutablePath = UseManagedClient\n            ? TunnelClientUpdateService.ManagedExecutablePath\n            : settings.TunnelClientPath?.Trim() ?? string.Empty;
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

    private static string? FindOnPath(string executable)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(entry.Trim('"'), executable);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch
            {
            }
        }

        return null;
    }
}
