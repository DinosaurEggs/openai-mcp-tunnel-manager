using System.Globalization;
using System.Text;

namespace OpenAITunnelManager.Core.Logging;

public static class AnsiLogParser
{
    private const char Escape = '\u001b';
    private const int MaxSgrParameters = 32;

    public static ParsedAnsiLine ParseLine(string raw, AnsiParserState state, int maxSpans = 256)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(state);
        maxSpans = Math.Max(1, maxSpans);

        var text = new StringBuilder(raw.Length);
        List<AnsiTextSpan>? spans = null;
        var style = state.Style;
        var rangeStart = 0;
        var spanOverflow = false;

        void CloseRange()
        {
            if (!style.IsDefault && text.Length > rangeStart && !spanOverflow)
            {
                spans ??= [];
                if (spans.Count >= maxSpans)
                {
                    spans.Clear();
                    spanOverflow = true;
                }
                else
                {
                    spans.Add(new AnsiTextSpan(rangeStart, text.Length - rangeStart, style));
                }
            }
            rangeStart = text.Length;
        }

        void ChangeStyle(AnsiTextStyle next)
        {
            if (next == style) return;
            CloseRange();
            style = next;
            rangeStart = text.Length;
        }

        for (var index = 0; index < raw.Length;)
        {
            var current = raw[index];
            if (current == Escape)
            {
                if (index + 1 >= raw.Length) break;
                var next = raw[index + 1];
                if (next == '[')
                {
                    var finalIndex = FindCsiFinal(raw, index + 2);
                    if (finalIndex < 0) break;
                    if (raw[finalIndex] == 'm')
                    {
                        ChangeStyle(ApplySgr(raw.AsSpan(index + 2, finalIndex - index - 2), style));
                    }
                    index = finalIndex + 1;
                    continue;
                }

                if (next == ']')
                {
                    index = SkipOsc(raw, index + 2);
                    continue;
                }

                // Unknown escape family: remove ESC itself and continue with the following text.
                index++;
                continue;
            }

            // Logs may contain terminal controls. Preserve tab only; other C0 controls are not executed.
            if (current < ' ' && current != '\t')
            {
                index++;
                continue;
            }

            text.Append(current);
            index++;
        }

        CloseRange();
        state.Style = style;
        var plain = text.ToString();
        return new ParsedAnsiLine(
            plain,
            DetectSeverity(plain),
            spanOverflow || spans is not { Count: > 0 } ? null : spans.ToArray());
    }

    private static int FindCsiFinal(string value, int start)
    {
        for (var index = start; index < value.Length; index++)
        {
            var current = value[index];
            if (current is >= '@' and <= '~') return index;
        }
        return -1;
    }

    private static int SkipOsc(string value, int start)
    {
        for (var index = start; index < value.Length; index++)
        {
            if (value[index] == '\a') return index + 1;
            if (value[index] == Escape && index + 1 < value.Length && value[index + 1] == '\\') return index + 2;
        }
        return value.Length;
    }

    private static AnsiTextStyle ApplySgr(ReadOnlySpan<char> parameterText, AnsiTextStyle style)
    {
        Span<int> values = stackalloc int[MaxSgrParameters];
        var count = ParseParameters(parameterText, values);
        if (count == 0)
        {
            values[0] = 0;
            count = 1;
        }

        for (var index = 0; index < count; index++)
        {
            var code = values[index];
            switch (code)
            {
                case 0:
                    style = default;
                    break;
                case 1:
                    style = style with { Bold = true };
                    break;
                case 2:
                    style = style with { Dim = true };
                    break;
                case 3:
                    style = style with { Italic = true };
                    break;
                case 4:
                    style = style with { Underline = true };
                    break;
                case 22:
                    style = style with { Bold = false, Dim = false };
                    break;
                case 23:
                    style = style with { Italic = false };
                    break;
                case 24:
                    style = style with { Underline = false };
                    break;
                case 39:
                    style = style with { Foreground = null };
                    break;
                case 49:
                    style = style with { Background = null };
                    break;
                case >= 30 and <= 37:
                    style = style with { Foreground = AnsiColor.Indexed((byte)(code - 30)) };
                    break;
                case >= 90 and <= 97:
                    style = style with { Foreground = AnsiColor.Indexed((byte)(8 + code - 90)) };
                    break;
                case >= 40 and <= 47:
                    style = style with { Background = AnsiColor.Indexed((byte)(code - 40)) };
                    break;
                case >= 100 and <= 107:
                    style = style with { Background = AnsiColor.Indexed((byte)(8 + code - 100)) };
                    break;
                case 38:
                case 48:
                {
                    var foreground = code == 38;
                    if (index + 2 < count && values[index + 1] == 5)
                    {
                        var color = AnsiColor.Indexed((byte)Math.Clamp(values[index + 2], 0, 255));
                        style = foreground ? style with { Foreground = color } : style with { Background = color };
                        index += 2;
                    }
                    else if (index + 4 < count && values[index + 1] == 2)
                    {
                        var color = AnsiColor.Rgb(
                            (byte)Math.Clamp(values[index + 2], 0, 255),
                            (byte)Math.Clamp(values[index + 3], 0, 255),
                            (byte)Math.Clamp(values[index + 4], 0, 255));
                        style = foreground ? style with { Foreground = color } : style with { Background = color };
                        index += 4;
                    }
                    break;
                }
            }
        }

        return style;
    }

    private static int ParseParameters(ReadOnlySpan<char> text, Span<int> destination)
    {
        if (text.IsEmpty) return 0;
        var count = 0;
        var start = 0;
        for (var index = 0; index <= text.Length && count < destination.Length; index++)
        {
            if (index != text.Length && text[index] != ';') continue;
            var token = text[start..index];
            if (token.IsEmpty)
            {
                destination[count++] = 0;
            }
            else if (int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                destination[count++] = value;
            }
            start = index + 1;
        }
        return count;
    }

    private static LogSeverity DetectSeverity(string value)
    {
        if (value.Contains("FATAL", StringComparison.OrdinalIgnoreCase)) return LogSeverity.Fatal;
        if (value.Contains("ERROR", StringComparison.OrdinalIgnoreCase)) return LogSeverity.Error;
        if (value.Contains("WARN", StringComparison.OrdinalIgnoreCase)) return LogSeverity.Warn;
        if (value.Contains("INFO", StringComparison.OrdinalIgnoreCase)) return LogSeverity.Info;
        if (value.Contains("DEBUG", StringComparison.OrdinalIgnoreCase)) return LogSeverity.Debug;
        if (value.Contains("TRACE", StringComparison.OrdinalIgnoreCase)) return LogSeverity.Trace;
        return LogSeverity.Unknown;
    }
}
