using System.Security.Cryptography;
using System.Text;
using OpenAITunnelManager.Core.Abstractions;

namespace OpenAITunnelManager.Core.Models;

public static class ConnectionScope
{
    public static string Profile(string path) => "profile:" + HashPath(path);

    public static string Primary(TunnelConnection item) => item.HasRuntime
        ? "runtime:" + HashPath(item.StateRoot) + ":" + item.RuntimeAlias
        : Profile(item.ProfilePath);

    public static IEnumerable<string> Keys(TunnelConnection item)
    {
        yield return Primary(item);
        if (item.HasRuntime && item.HasProfile) yield return Profile(item.ProfilePath);
    }

    // Migrate only references which identify exactly one destination in the initial
    // inventory. Legacy entries are retained for recovery but never used as fallback.
    public static int MigrateLegacy(IReadOnlyList<TunnelConnection> items, AppSettings settings, ICredentialStore credentials)
    {
        var bare = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var typed = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        static void Add(Dictionary<string, HashSet<string>> map, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            if (!map.TryGetValue(key, out var values))
                map[key] = values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            values.Add(value);
        }
        foreach (var item in items)
        {
            if (item.HasRuntime)
            {
                Add(typed, item.Identity, Primary(item));
                Add(bare, item.RuntimeAlias, Primary(item));
            }
            if (item.HasProfile)
            {
                Add(typed, "profile:" + item.ProfileName, Profile(item.ProfilePath));
                Add(bare, item.ProfileName, Profile(item.ProfilePath));
            }
        }

        foreach (var pair in typed.Concat(bare))
            if (pair.Value.Count == 1 && settings.ProfilePreferences.TryGetValue(pair.Key, out var old))
                settings.ProfilePreferences.TryAdd(pair.Value.Single(), old);

        var ambiguous = 0;
        foreach (var pair in bare)
        {
            var secret = credentials.Get(pair.Key);
            if (string.IsNullOrEmpty(secret)) continue;
            if (pair.Value.Count != 1) { ambiguous++; continue; }
            var target = pair.Value.Single();
            if (string.IsNullOrEmpty(credentials.Get(target))) credentials.Set(target, secret);
        }
        return ambiguous;
    }

    private static string HashPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
