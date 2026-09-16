using System.Text.RegularExpressions;

namespace OpenAITunnelManager.Core.Models;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 2;
    public string TunnelClientPath { get; set; } = string.Empty;
    public bool CloseToTray { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public string ProfileDirectoryOverride { get; set; } = string.Empty;
    public string StateDirectoryOverride { get; set; } = string.Empty;
    public Dictionary<string, ProfilePreference> ProfilePreferences { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public ProfilePreference GetPreference(TunnelConnection connection)
    {
        foreach (var key in PreferenceKeys(connection))
        {
            if (ProfilePreferences.TryGetValue(key, out var preference))
            {
                return preference;
            }
        }

        var created = new ProfilePreference();
        ProfilePreferences[connection.HasProfile ? $"profile:{connection.ProfileName}" : connection.Identity] = created;
        return created;
    }

    public string GetPreferenceKey(TunnelConnection connection) =>
        connection.HasProfile ? $"profile:{connection.ProfileName}" : connection.Identity;

    private static IEnumerable<string> PreferenceKeys(TunnelConnection connection)
    {
        yield return connection.Identity;
        if (connection.HasProfile)
        {
            yield return $"profile:{connection.ProfileName}";
            yield return connection.ProfileName;
        }

        if (connection.HasRuntime)
        {
            yield return connection.RuntimeAlias;
        }
    }
}

public sealed class ProfilePreference
{
    public bool AutoConnect { get; set; }
    public bool AutoReconnect { get; set; }
    public bool Enabled { get; set; } = true;
}

public enum McpType
{
    Http,
    Stdio
}

public sealed record ProfileSpec(string Name, string TunnelId, McpType McpType, string McpTarget)
{
    private static readonly Regex ProfileNamePattern = new(
        @"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex TunnelIdPattern = new(
        @"^tunnel_[0-9a-f]{32}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        var name = Name.Trim();
        var tunnelId = TunnelId.Trim();
        var target = McpTarget.Trim();

        if (!ProfileNamePattern.IsMatch(name))
        {
            errors.Add("Profile 名称必须以字母或数字开头，只能包含字母、数字、.、_、-，最长 128 字符");
        }

        if (!TunnelIdPattern.IsMatch(tunnelId))
        {
            errors.Add("Tunnel ID 必须是 tunnel_ 加 32 个小写十六进制字符");
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            errors.Add("MCP 地址/命令不能为空");
        }
        else if (McpType == McpType.Http)
        {
            if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                string.IsNullOrWhiteSpace(uri.Host))
            {
                errors.Add("HTTP MCP 地址必须是有效的 http:// 或 https:// URL");
            }
        }
        else if (target.Length > 4096)
        {
            errors.Add("STDIO 命令最长 4096 字符");
        }

        return errors;
    }
}

public sealed record TunnelClientCapabilities(
    string Version,
    bool Profiles,
    bool Runtimes,
    bool Doctor);
