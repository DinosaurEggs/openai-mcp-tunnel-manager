using System.Text.Json;
using YamlDotNet.RepresentationModel;

namespace OpenAITunnelManager.Infrastructure.TunnelClient;

public static class ProfileDocumentEditor
{
    public static ProfileMetadataView ReadMetadata(string text)
    {
        _ = ReadYamlRoot(text);
        var metadata = ProfileMetadataReader.Parse(text);
        return new ProfileMetadataView(metadata.TunnelId, metadata.TargetKind, metadata.TargetValue, metadata.ApiKeyRef);
    }

    public static string ApplyCommonFields(string text, string originalTunnelId, string originalTargetKind, string originalTargetValue, string tunnelId, string targetValue)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Profile 内容不能为空", nameof(text));
        var changedTunnel = !string.Equals(tunnelId, originalTunnelId, StringComparison.Ordinal);
        var changedTarget = !string.Equals(targetValue, originalTargetValue, StringComparison.Ordinal);
        if (!changedTunnel && !changedTarget) return text;
        if (TryApplyJson(text, changedTunnel, changedTarget, originalTargetKind, tunnelId, targetValue, out var json)) return json;
        var result = text;
        if (changedTunnel) result = ReplaceYamlSectionScalar(result, "control_plane", "tunnel_id", tunnelId);
        if (changedTarget)
        {
            if (originalTargetKind is not ("server_url" or "command")) throw new InvalidOperationException("当前 Profile 的 main MCP 绑定不是常用格式，请在高级配置中直接修改");
            result = ReplaceYamlMainTarget(result, originalTargetKind, targetValue);
        }
        return result;
    }

    public static string? ReadSectionScalar(string text, string section, string key)
    {
        var node = Child(Child(ReadYamlRoot(text), section) as YamlMappingNode, key);
        return node is YamlScalarNode scalar ? scalar.Value : null;
    }

    public static string SetSectionScalar(string text, string section, string key, string? value, bool boolean = false)
    {
        var root = ReadYamlRoot(text);
        if (boolean && value is not null && value is not ("true" or "false"))
            throw new ArgumentException($"{section}.{key} 必须为 true 或 false");
        if (text.TrimStart().StartsWith('{'))
        {
            var json = System.Text.Json.Nodes.JsonNode.Parse(text) as System.Text.Json.Nodes.JsonObject
                ?? throw new InvalidOperationException("Profile 必须是对象");
            if (json[section] is null)
            {
                if (value is null) return text;
                json[section] = new System.Text.Json.Nodes.JsonObject();
            }
            if (json[section] is not System.Text.Json.Nodes.JsonObject map)
                throw new InvalidOperationException($"{section} 不是对象");
            if (value is null) map.Remove(key);
            else if (boolean) map[key] = value == "true";
            else map[key] = value;
            return json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
        }

        var sectionNode = Child(root, section);
        var literal = boolean ? value : JsonSerializer.Serialize(value);
        if (sectionNode is null)
        {
            if (value is null) return text;
            // Insert before the first root entry rather than after a possible document end marker.
            var first = root.Children.Keys.FirstOrDefault()
                ?? throw new InvalidOperationException("请在高级配置中添加字段");
            return InsertBeforeLine(text, first, $"{section}:{Environment.NewLine}  {key}: {literal}");
        }
        if (sectionNode is not YamlMappingNode sectionMap)
            throw new InvalidOperationException($"{section} 不是对象");
        var entry = sectionMap.Children.FirstOrDefault(pair => pair.Key is YamlScalarNode k && k.Value == key);
        if (entry.Key is null)
        {
            if (value is null) return text;
            var first = sectionMap.Children.Keys.FirstOrDefault()
                ?? throw new InvalidOperationException("空映射请在高级配置中修改");
            return InsertBeforeLine(text, first, new string(' ', checked((int)first.Start.Column - 1)) + $"{key}: {literal}");
        }
        if (entry.Value is not YamlScalarNode scalarValue || !scalarValue.Anchor.IsEmpty)
            throw new InvalidOperationException($"请在高级配置中修改 {section}.{key}");
        var start = checked((int)scalarValue.Start.Index);
        var end = checked((int)scalarValue.End.Index);
        if (start < 0 || end <= start || end > text.Length || text.AsSpan(start, end - start).IndexOfAny('\r', '\n') >= 0)
            throw new InvalidOperationException("多行字段请在高级配置中修改");
        if (value is not null) return text[..start] + literal + text[end..];
        var keyStart = checked((int)entry.Key.Start.Index);
        var lineStart = text.LastIndexOf('\n', Math.Max(0, keyStart - 1)) + 1;
        var lineEnd = text.IndexOf('\n', end);
        if (lineEnd < 0) lineEnd = text.Length;
        var suffix = text[end..lineEnd].Trim();
        if (!string.IsNullOrWhiteSpace(text[lineStart..keyStart]) || (suffix.Length != 0 && !suffix.StartsWith('#')))
            throw new InvalidOperationException("请在高级配置中删除该字段");
        var replacement = suffix.Length == 0 ? string.Empty : text[lineStart..keyStart] + suffix + Environment.NewLine;
        return text[..lineStart] + replacement + text[(lineEnd < text.Length ? lineEnd + 1 : lineEnd)..];
    }

    private static string InsertBeforeLine(string text, YamlNode node, string line)
    {
        var index = checked((int)node.Start.Index);
        var start = text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
        if (!string.IsNullOrWhiteSpace(text[start..index]))
            throw new InvalidOperationException("行内 YAML 映射请在高级配置中修改");
        return text.Insert(start, line + Environment.NewLine);
    }

    private static bool TryApplyJson(string text, bool changedTunnel, bool changedTarget, string targetKind, string tunnelId, string targetValue, out string result)
    {
        result = string.Empty;
        try { using var validation = JsonDocument.Parse(text); }
        catch (JsonException) { return false; }
        var node = System.Text.Json.Nodes.JsonNode.Parse(text) as System.Text.Json.Nodes.JsonObject ?? throw new InvalidOperationException("Profile JSON 根节点不是对象");
        if (changedTunnel)
        {
            if (node["control_plane"] is not System.Text.Json.Nodes.JsonObject cp) throw new InvalidOperationException("高级配置中缺少 control_plane 对象");
            cp["tunnel_id"] = tunnelId;
        }
        if (changedTarget)
        {
            if (node["mcp"] is not System.Text.Json.Nodes.JsonObject mcp) throw new InvalidOperationException("高级配置中缺少 mcp 对象");
            var arrayName = targetKind == "server_url" ? "server_urls" : "commands";
            var valueName = targetKind == "server_url" ? "url" : "command";
            if (mcp[arrayName] is not System.Text.Json.Nodes.JsonArray array) throw new InvalidOperationException("没有找到可安全修改的 main MCP 绑定");
            var entry = array.OfType<System.Text.Json.Nodes.JsonObject>().FirstOrDefault(item =>
            {
                var channel = item["channel"]?.GetValue<string>();
                return string.IsNullOrWhiteSpace(channel) || string.Equals(channel, "main", StringComparison.OrdinalIgnoreCase);
            }) ?? throw new InvalidOperationException("没有找到可安全修改的 main MCP 绑定");
            entry[valueName] = targetValue;
        }
        result = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
        return true;
    }

    private static YamlMappingNode ReadYamlRoot(string text)
    {
        var yaml = new YamlStream();
        using var reader = new StringReader(text);
        yaml.Load(reader);
        if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode root)
            throw new InvalidOperationException("Profile 必须包含一个 YAML 对象");
        return root;
    }

    private static YamlNode? Child(YamlMappingNode? map, string key)
    {
        if (map is null) return null;
        return map.Children.TryGetValue(new YamlScalarNode(key), out var node) ? node : null;
    }

    private static string ReplaceYamlSectionScalar(string text, string section, string key, string value)
    {
        var root = ReadYamlRoot(text);
        var node = Child(Child(root, section) as YamlMappingNode, key) as YamlScalarNode;
        return ReplaceScalar(text, node, value);
    }

    private static string ReplaceYamlMainTarget(string text, string targetKind, string value)
    {
        var root = ReadYamlRoot(text);
        var mcp = Child(root, "mcp") as YamlMappingNode;
        var arrayName = targetKind == "server_url" ? "server_urls" : "commands";
        var key = targetKind == "server_url" ? "url" : "command";
        var entries = Child(mcp, arrayName) as YamlSequenceNode;
        var candidates = entries?.Children.OfType<YamlMappingNode>()
            .Where(entry =>
            {
                var channel = (Child(entry, "channel") as YamlScalarNode)?.Value;
                return string.IsNullOrWhiteSpace(channel) || string.Equals(channel, "main", StringComparison.OrdinalIgnoreCase);
            })
            .Select(entry => Child(entry, key) as YamlScalarNode)
            .Where(node => node is not null).ToArray() ?? [];
        if (candidates.Length != 1)
            throw new InvalidOperationException("无法确认唯一 main MCP 绑定，请直接修改高级配置");
        return ReplaceScalar(text, candidates[0], value);
    }

    private static string ReplaceScalar(string text, YamlScalarNode? node, string value)
    {
        if (node is null || !node.Anchor.IsEmpty)
            throw new InvalidOperationException("无法安全修改 YAML 字段，请直接修改高级配置");
        var start = checked((int)node.Start.Index);
        var end = checked((int)node.End.Index);
        if (start < 0 || end <= start || end > text.Length ||
            text.AsSpan(start, end - start).IndexOfAny('\r', '\n') >= 0)
            throw new InvalidOperationException("请在高级配置中修改多行 YAML 字段");
        return text[..start] + JsonSerializer.Serialize(value) + text[end..];
    }
}

public sealed record ProfileMetadataView(string TunnelId, string TargetKind, string TargetValue, string ApiKeyRef);
