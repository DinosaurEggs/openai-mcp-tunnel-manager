namespace OpenAITunnelManager.Core.Logging;

public sealed record StructuredLogEntry(
    long Sequence,
    LogSeverity Severity,
    DateTimeOffset? Timestamp,
    string Message,
    string Logger,
    string RawText,
    IReadOnlyDictionary<string, string>? Fields)
{
    public string DuplicateKey => $"{Severity}\u001f{Message}";

    public string ToConsoleText()
    {
        var timestamp = Timestamp?.ToLocalTime().ToString("HH:mm:ss.fff") ?? "--:--:--.---";
        var level = Severity switch
        {
            LogSeverity.Trace => "TRACE",
            LogSeverity.Debug => "DEBUG",
            LogSeverity.Info => "INFO ",
            LogSeverity.Warn => "WARN ",
            LogSeverity.Error => "ERROR",
            LogSeverity.Fatal => "FATAL",
            _ => "     "
        };
        var logger = string.IsNullOrWhiteSpace(Logger) ? string.Empty : $" [{Logger}]";
        return $"{timestamp} {level}{logger} {Message}".TrimEnd();
    }
}

public sealed record LogDisplayEntry(StructuredLogEntry Entry, int RepeatCount)
{
    public string ToConsoleText() => RepeatCount > 1
        ? $"{Entry.ToConsoleText()}  ×{RepeatCount}"
        : Entry.ToConsoleText();
}
