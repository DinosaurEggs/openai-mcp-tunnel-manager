using System.Text.Json;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace OpenAITunnelManager.Infrastructure.TunnelClient;

public static class ProfileDocumentEditor
{
    public static ProfileMetadataView ReadMetadata(string text)
    {
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

    private static string ReplaceYamlSectionScalar(string text, string section, string key, string value)
    {
        var lines = SplitKeepEndings(text); var sectionIndex = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            var stripped = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(stripped) || lines[i].TrimStart().StartsWith('#')) continue;
            if (LeadingSpaces(lines[i]) == 0 && stripped == section + ":") { sectionIndex = i; break; }
        }
        if (sectionIndex < 0) throw new InvalidOperationException($"高级配置中缺少 {section}: 段，无法安全应用常用设置");
        var end = lines.Count;
        for (var i = sectionIndex + 1; i < lines.Count; i++)
        {
            var stripped = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(stripped) || lines[i].TrimStart().StartsWith('#')) continue;
            if (LeadingSpaces(lines[i]) == 0) { end = i; break; }
        }
        var pattern = new Regex($@"^(\s*){Regex.Escape(key)}\s*:", RegexOptions.CultureInvariant);
        for (var i = sectionIndex + 1; i < end; i++)
        {
            var match = pattern.Match(lines[i]); if (!match.Success) continue;
            lines[i] = $"{match.Groups[1].Value}{key}: {JsonSerializer.Serialize(value)}{GetNewLine(lines[i])}";
            return string.Concat(lines);
        }
        var nl = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        lines.Insert(sectionIndex + 1, $"  {key}: {JsonSerializer.Serialize(value)}{nl}");
        return string.Concat(lines);
    }

    private static string ReplaceYamlMainTarget(string text, string targetKind, string value)
    {
        var lines = SplitKeepEndings(text); var section = string.Empty; var mode = string.Empty; var channel = "main";
        var wantedMode = targetKind == "server_url" ? "server_url" : "command"; var wantedKey = wantedMode == "server_url" ? "url" : "command";
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i]; var stripped = line.Trim();
            if (string.IsNullOrWhiteSpace(stripped) || line.TrimStart().StartsWith('#')) continue;
            var indent = LeadingSpaces(line);
            if (indent == 0 && stripped.EndsWith(':')) { section = stripped[..^1]; mode = string.Empty; channel = "main"; continue; }
            if (section != "mcp") continue;
            if (stripped == "server_urls:") { mode = "server_url"; channel = "main"; continue; }
            if (stripped == "commands:") { mode = "command"; channel = "main"; continue; }
            var cm = Regex.Match(stripped, @"^-?\s*channel\s*:\s*(.+)$");
            if (cm.Success && !string.IsNullOrEmpty(mode)) { channel = ParseYamlScalar(cm.Groups[1].Value); continue; }
            if (mode != wantedMode || !string.Equals(channel, "main", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Regex.IsMatch(stripped, $@"^{Regex.Escape(wantedKey)}\s*:")) continue;
            lines[i] = $"{new string(' ', indent)}{wantedKey}: {JsonSerializer.Serialize(value)}{GetNewLine(line)}";
            return string.Concat(lines);
        }
        throw new InvalidOperationException("没有找到可安全修改的 main MCP 绑定；请直接在高级配置中修改完整 YAML");
    }

    private static string ParseYamlScalar(string value)
    {
        try
        {
            using var reader = new StringReader($"value: {value}"); var yaml = new YamlStream(); yaml.Load(reader);
            if (yaml.Documents[0].RootNode is YamlMappingNode map)
                foreach (var pair in map.Children)
                    if (pair.Key is YamlScalarNode key && key.Value == "value" && pair.Value is YamlScalarNode scalar) return scalar.Value?.Trim() ?? string.Empty;
        }
        catch { }
        return value.Trim().Trim('"', '\'');
    }

    private static List<string> SplitKeepEndings(string text)
    {
        var result = new List<string>(); var start = 0;
        for (var i = 0; i < text.Length; i++) if (text[i] == '\n') { result.Add(text[start..(i + 1)]); start = i + 1; }
        if (start < text.Length) result.Add(text[start..]); if (result.Count == 0) result.Add(text); return result;
    }
    private static int LeadingSpaces(string line) { var count = 0; while (count < line.Length && line[count] == ' ') count++; return count; }
    private static string GetNewLine(string line) => line.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n" : line.EndsWith('\n') ? "\n" : string.Empty;
}

public sealed record ProfileMetadataView(string TunnelId, string TargetKind, string TargetValue, string ApiKeyRef);
