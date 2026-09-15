using YamlDotNet.RepresentationModel;

namespace OpenAITunnelManager.Infrastructure.TunnelClient;

internal sealed record ProfileMetadata(string TunnelId, string TargetKind, string TargetValue)
{
    public static ProfileMetadata Empty { get; } = new(string.Empty, string.Empty, string.Empty);
}

internal static class ProfileMetadataReader
{
    public static ProfileMetadata Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return ProfileMetadata.Empty;
        }

        try
        {
            using var reader = File.OpenText(path);
            var yaml = new YamlStream();
            yaml.Load(reader);
            if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
            {
                return ProfileMetadata.Empty;
            }

            var tunnelId = GetScalar(GetMapping(root, "control_plane"), "tunnel_id");
            var mcp = GetMapping(root, "mcp");

            var serverUrl = FindMainTarget(GetSequence(mcp, "server_urls"), "url");
            if (!string.IsNullOrWhiteSpace(serverUrl))
            {
                return new ProfileMetadata(tunnelId, "server_url", serverUrl);
            }

            var command = FindMainTarget(GetSequence(mcp, "commands"), "command");
            return string.IsNullOrWhiteSpace(command)
                ? new ProfileMetadata(tunnelId, string.Empty, string.Empty)
                : new ProfileMetadata(tunnelId, "command", command);
        }
        catch (IOException)
        {
            return ProfileMetadata.Empty;
        }
        catch (YamlDotNet.Core.YamlException)
        {
            return ProfileMetadata.Empty;
        }
    }

    private static string FindMainTarget(YamlSequenceNode? entries, string valueKey)
    {
        if (entries is null)
        {
            return string.Empty;
        }

        foreach (var node in entries.Children.OfType<YamlMappingNode>())
        {
            var channel = GetScalar(node, "channel");
            if (!string.IsNullOrWhiteSpace(channel) && !string.Equals(channel, "main", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = GetScalar(node, valueKey);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static YamlMappingNode? GetMapping(YamlMappingNode? node, string key) =>
        TryGet(node, key) as YamlMappingNode;

    private static YamlSequenceNode? GetSequence(YamlMappingNode? node, string key) =>
        TryGet(node, key) as YamlSequenceNode;

    private static string GetScalar(YamlMappingNode? node, string key) =>
        (TryGet(node, key) as YamlScalarNode)?.Value?.Trim() ?? string.Empty;

    private static YamlNode? TryGet(YamlMappingNode? node, string key)
    {
        if (node is null)
        {
            return null;
        }

        foreach (var pair in node.Children)
        {
            if (pair.Key is YamlScalarNode scalar && string.Equals(scalar.Value, key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }
}
