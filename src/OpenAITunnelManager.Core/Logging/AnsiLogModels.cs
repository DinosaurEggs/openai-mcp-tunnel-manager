namespace OpenAITunnelManager.Core.Logging;

public enum LogSeverity
{
    Unknown,
    Trace,
    Debug,
    Info,
    Warn,
    Error,
    Fatal
}

public enum AnsiColorKind
{
    Indexed,
    Rgb
}

public readonly record struct AnsiColor(AnsiColorKind Kind, byte Index, byte R, byte G, byte B)
{
    public static AnsiColor Indexed(byte index) => new(AnsiColorKind.Indexed, index, 0, 0, 0);
    public static AnsiColor Rgb(byte red, byte green, byte blue) => new(AnsiColorKind.Rgb, 0, red, green, blue);
}

public readonly record struct AnsiTextStyle(
    AnsiColor? Foreground,
    AnsiColor? Background,
    bool Bold,
    bool Dim,
    bool Italic,
    bool Underline)
{
    public bool IsDefault =>
        Foreground is null &&
        Background is null &&
        !Bold &&
        !Dim &&
        !Italic &&
        !Underline;
}

public readonly record struct AnsiTextSpan(int Start, int Length, AnsiTextStyle Style);

public sealed record ParsedAnsiLine(
    string Text,
    LogSeverity Severity,
    IReadOnlyList<AnsiTextSpan>? Spans);

// WinUI's generated XAML type metadata writes public properties when a model is used by a
// DataTemplate. Keep this as a small mutable binding DTO rather than an init-only record.
public sealed class LogLine
{
    public LogLine()
    {
    }

    public LogLine(long sequence, string text, LogSeverity severity, IReadOnlyList<AnsiTextSpan>? spans)
    {
        Sequence = sequence;
        Text = text;
        Severity = severity;
        Spans = spans;
    }

    public long Sequence { get; set; }
    public string Text { get; set; } = string.Empty;
    public LogSeverity Severity { get; set; }
    public IReadOnlyList<AnsiTextSpan>? Spans { get; set; }
    public bool HasAnsi => Spans is { Count: > 0 };
}

public sealed class AnsiParserState
{
    public AnsiTextStyle Style { get; internal set; }

    public void Reset() => Style = default;

    public AnsiParserState Clone() => new() { Style = Style };
}
