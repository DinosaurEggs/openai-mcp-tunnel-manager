using YamlDotNet.RepresentationModel;

namespace OpenAITunnelManager.Infrastructure.TunnelClient;

// Native runtimes connect regenerates the profile. Reject configuration that the
// existing command adapter cannot reproduce before invoking the destructive CLI.
internal static class NativeRuntimeProfileGuard
{
    public static IReadOnlyList<string> Validate(string text, string stateRoot, string alias)
    {
        var yaml = new YamlStream();
        using var reader = new StringReader(text);
        yaml.Load(reader);
        if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode root)
            throw Unsupported();

        RequireKeys(root, "config_version", "control_plane", "mcp", "health", "log", "admin_ui");
        if (Scalar(root, "config_version") != "1") throw Unsupported();
        var control = Mapping(root, "control_plane");
        RequireKeys(control, "tunnel_id", "api_key", "base_url", "url_path", "residency");
        if (string.IsNullOrWhiteSpace(Scalar(control, "tunnel_id"))) throw Unsupported();
        var keyRef = Scalar(control, "api_key");
        if (!string.IsNullOrEmpty(keyRef) &&
            TunnelClientProcessRunner.SecretEnvironmentName(keyRef) is null &&
            !keyRef.StartsWith("file:", StringComparison.Ordinal)) throw Unsupported();
        var flags = new List<string>();
        foreach (var (key, flag) in new[]
        {
            ("base_url", "--control-plane-base-url"),
            ("url_path", "--control-plane-url-path"),
            ("residency", "--control-plane-residency")
        })
        {
            var value = Scalar(control, key);
            if (!string.IsNullOrWhiteSpace(value)) flags.AddRange([flag, value]);
        }

        var mcp = Mapping(root, "mcp");
        RequireKeys(mcp, "server_urls", "commands");
        if (mcp.Children.Count != 1) throw Unsupported();
        var kind = mcp.Children.ContainsKey(new YamlScalarNode("server_urls")) ? "server_urls" : "commands";
        if (Child(mcp, kind) is not YamlSequenceNode sequence ||
            sequence.Children.Count != 1 || sequence.Children[0] is not YamlMappingNode binding)
            throw Unsupported();
        var target = kind == "server_urls" ? "url" : "command";
        RequireKeys(binding, "channel", target);
        var channel = Scalar(binding, "channel");
        if ((!string.IsNullOrEmpty(channel) && channel != "main") ||
            string.IsNullOrWhiteSpace(Scalar(binding, target))) throw Unsupported();

        if (Child(root, "health") is not null)
        {
            var health = Mapping(root, "health");
            RequireKeys(health, "listen_addr", "url_file");
            if (Scalar(health, "listen_addr") != "127.0.0.1:0" ||
                !MatchesManagedPath(Scalar(health, "url_file"), stateRoot, "health", alias + ".url"))
                throw Unsupported();
        }

        if (Child(root, "log") is not null)
        {
            var log = Mapping(root, "log");
            RequireKeys(log, "level", "format", "file");
            if (Scalar(log, "level") != "info" || Scalar(log, "format") != "json" ||
                !MatchesManagedPath(Scalar(log, "file"), stateRoot, "logs", alias + ".log"))
                throw Unsupported();
        }

        if (Child(root, "admin_ui") is not null)
        {
            var admin = Mapping(root, "admin_ui");
            RequireKeys(admin, "open_browser");
            if (Scalar(admin, "open_browser") != "false") throw Unsupported();
        }
        return flags;
    }

    private static void RequireKeys(YamlMappingNode map, params string[] allowed)
    {
        if (map.Children.Keys.Any(key => key is not YamlScalarNode scalar ||
                scalar.Value is null || !allowed.Contains(scalar.Value, StringComparer.Ordinal)))
            throw Unsupported();
    }

    private static YamlNode? Child(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var node) ? node : null;

    private static YamlMappingNode Mapping(YamlMappingNode map, string key) =>
        Child(map, key) as YamlMappingNode ?? throw Unsupported();

    private static string Scalar(YamlMappingNode map, string key)
    {
        var node = Child(map, key);
        if (node is null) return string.Empty;
        return node is YamlScalarNode scalar ? scalar.Value ?? string.Empty : throw Unsupported();
    }

    private static bool MatchesManagedPath(string path, string stateRoot, string directory, string name) =>
        !string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(stateRoot) &&
        string.Equals(Path.GetFullPath(path), Path.GetFullPath(Path.Combine(stateRoot, directory, name)),
            StringComparison.OrdinalIgnoreCase);

    private static InvalidOperationException Unsupported() =>
        new("该 Profile 包含 runtimes connect 无法无损保留的配置，请使用 Profile 前台运行方式。");
}
