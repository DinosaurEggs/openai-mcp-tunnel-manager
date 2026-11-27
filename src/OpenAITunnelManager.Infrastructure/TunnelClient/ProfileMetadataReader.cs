using YamlDotNet.RepresentationModel;

namespace OpenAITunnelManager.Infrastructure.TunnelClient;

internal sealed record ProfileMetadata(
    string TunnelId,
    string ApiKeyRef,
    string TargetKind,
    string TargetValue)
{
    public bool UsesAwsIdentity { get; init; }
    public string Residency { get; init; } = string.Empty;
    public string AdminBasePath { get; init; } = string.Empty;
    public string Authentication => UsesAwsIdentity ? "AWS Workload Identity" : ApiKeyRef.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? "文件引用" : "API Key / 环境变量";
    public const string DefaultApiKeyRef = "env:CONTROL_PLANE_API_KEY";
    public static ProfileMetadata Empty { get; } =
        new(string.Empty, DefaultApiKeyRef, string.Empty, string.Empty);
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
            return Parse(File.ReadAllText(path));
        }
        catch (IOException)
        {
            return ProfileMetadata.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return ProfileMetadata.Empty;
        }
    }

    public static ProfileMetadata Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ProfileMetadata.Empty;
        }

        try
        {
            using var reader = new StringReader(text);
            var yaml = new YamlStream();
            yaml.Load(reader);
            if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
            {
                return ProfileMetadata.Empty;
            }

            var controlPlane = GetMapping(root, "control_plane");
            var tunnelId = GetScalar(controlPlane, "tunnel_id");
            var usesAwsIdentity = !string.IsNullOrWhiteSpace(GetScalar(controlPlane, "aws_identity_provider_id")) ||
                !string.IsNullOrWhiteSpace(GetScalar(controlPlane, "aws_service_account_id"));
            var apiKeyRef = usesAwsIdentity ? string.Empty : GetScalar(controlPlane, "api_key");
            if (!usesAwsIdentity && string.IsNullOrWhiteSpace(apiKeyRef))
            {
                apiKeyRef = ProfileMetadata.DefaultApiKeyRef;
            }

            ProfileMetadata Build(string kind, string value) => new(tunnelId, apiKeyRef, kind, value)
            {
                UsesAwsIdentity = usesAwsIdentity,
                Residency = GetScalar(controlPlane, "residency"),
                AdminBasePath = GetScalar(GetMapping(root, "admin_ui"), "base_path")
            };
            var mcp = GetMapping(root, "mcp");
            var serverUrl = FindMainTarget(GetSequence(mcp, "server_urls"), "url");
            if (!string.IsNullOrWhiteSpace(serverUrl))
            {
                return Build("server_url", serverUrl);
            }

            var command = FindMainTarget(GetSequence(mcp, "commands"), "command");
            return string.IsNullOrWhiteSpace(command)
                ? Build(string.Empty, string.Empty)
                : Build("command", command);
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
            if (!string.IsNullOrWhiteSpace(channel) &&
                !string.Equals(channel, "main", StringComparison.OrdinalIgnoreCase))
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
            if (pair.Key is YamlScalarNode scalar &&
                string.Equals(scalar.Value, key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }
}
