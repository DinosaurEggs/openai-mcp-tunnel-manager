using System.Globalization;
using System.Text.Json;

namespace OpenAITunnelManager.Core.Logging;

public static class JsonLogParser
{
    private static readonly string[] TimestampNames = ["timestamp", "time", "ts", "@timestamp"];
    private static readonly string[] MessageNames = ["message", "msg"];
    private static readonly string[] LoggerNames = ["logger", "target", "component", "module"];

    public static StructuredLogEntry ParseLine(string raw, long sequence = 0)
    {
        ArgumentNullException.ThrowIfNull(raw);

        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Unknown(raw, sequence);

            var level = ReadString(root, "level");
            var message = ReadFirstString(root, MessageNames);
            var logger = ReadFirstString(root, LoggerNames);
            var timestamp = ReadTimestamp(root);
            var fields = ReadFields(root);

            if (string.IsNullOrWhiteSpace(message))
                message = raw;

            return new StructuredLogEntry(
                sequence,
                ParseSeverity(level),
                timestamp,
                message,
                logger,
                raw,
                fields);
        }
        catch (JsonException)
        {
            return Unknown(raw, sequence);
        }
    }

    public static LogSeverity ParseSeverity(string? level) => level?.Trim().ToLowerInvariant() switch
    {
        "trace" => LogSeverity.Trace,
        "debug" => LogSeverity.Debug,
        "info" => LogSeverity.Info,
        "warn" or "warning" => LogSeverity.Warn,
        "error" => LogSeverity.Error,
        "fatal" or "critical" => LogSeverity.Fatal,
        _ => LogSeverity.Unknown
    };

    private static StructuredLogEntry Unknown(string raw, long sequence) =>
        new(sequence, LogSeverity.Unknown, null, raw, string.Empty, raw, null);

    private static DateTimeOffset? ReadTimestamp(JsonElement root)
    {
        foreach (var name in TimestampNames)
        {
            if (!TryProperty(root, name, out var value)) continue;

            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (DateTimeOffset.TryParse(
                        text,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                        out var parsed))
                {
                    return parsed;
                }
            }
            else if (value.ValueKind == JsonValueKind.Number)
            {
                if (value.TryGetInt64(out var number))
                {
                    try
                    {
                        return number > 10_000_000_000L
                            ? DateTimeOffset.FromUnixTimeMilliseconds(number)
                            : DateTimeOffset.FromUnixTimeSeconds(number);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                    }
                }
            }
        }

        return null;
    }

    private static string ReadFirstString(JsonElement root, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            var value = ReadString(root, name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return string.Empty;
    }

    private static string ReadString(JsonElement root, string name)
    {
        if (!TryProperty(root, name, out var value)) return string.Empty;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim() ?? string.Empty,
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
            _ => string.Empty
        };
    }

    private static IReadOnlyDictionary<string, string> ReadFields(JsonElement root)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in root.EnumerateObject())
        {
            result[property.Name] = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString() ?? string.Empty
                : property.Value.GetRawText();
        }
        return result;
    }

    private static bool TryProperty(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
