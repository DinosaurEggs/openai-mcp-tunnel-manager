using System.Text.RegularExpressions;

namespace OpenAITunnelManager.Infrastructure.TunnelClient;

public static class TunnelClientVersionPolicy
{
    public const string MinimumSupportedVersion = "v0.0.21";
    private static readonly Version Minimum = new(0, 0, 21);

    public static bool IsSupported(string? reported)
    {
        // The official CLI prints the release tag, optionally followed by a git SHA.
        var match = Regex.Match(reported ?? string.Empty, @"^\s*v?(\d+\.\d+\.\d+)(?:\s|$)");
        return match.Success && Version.TryParse(match.Groups[1].Value, out var version) && version >= Minimum;
    }

    public static void RequireSupported(string? reported)
    {
        if (!IsSupported(reported))
            throw new InvalidDataException($"需要 tunnel-client {MinimumSupportedVersion} 或更新的正式版本，当前版本：{reported}");
    }
}
