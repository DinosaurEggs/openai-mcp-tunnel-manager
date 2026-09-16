using OpenAITunnelManager.Core.Logging;
using Xunit;

namespace OpenAITunnelManager.Tests;

public sealed class AnsiLogParserTests
{
    [Fact]
    public void ParsesStandardColorAndUnicodeWithoutKeepingEscapeCodes()
    {
        var state = new AnsiParserState();
        var parsed = AnsiLogParser.ParseLine("\u001b[32mINFO\u001b[0m connected ✅ 中文 😀", state);

        Assert.Equal("INFO connected ✅ 中文 😀", parsed.Text);
        Assert.Equal(LogSeverity.Info, parsed.Severity);
        var span = Assert.Single(parsed.Spans!);
        Assert.Equal(0, span.Start);
        Assert.Equal(4, span.Length);
        Assert.Equal(AnsiColor.Indexed(2), span.Style.Foreground);
    }

    [Fact]
    public void Parses256ColorTrueColorAndBackground()
    {
        var state = new AnsiParserState();
        var parsed = AnsiLogParser.ParseLine(
            "\u001b[38;5;202mA\u001b[48;2;10;20;30mB\u001b[0mC",
            state);

        Assert.Equal("ABC", parsed.Text);
        Assert.NotNull(parsed.Spans);
        Assert.Equal(2, parsed.Spans!.Count);
        Assert.Equal(AnsiColor.Indexed(202), parsed.Spans[0].Style.Foreground);
        Assert.Equal(AnsiColor.Rgb(10, 20, 30), parsed.Spans[1].Style.Background);
    }

    [Fact]
    public void CarriesSgrStateAcrossLinesAndResetsExplicitly()
    {
        var state = new AnsiParserState();
        var first = AnsiLogParser.ParseLine("\u001b[31mfirst", state);
        var second = AnsiLogParser.ParseLine("second\u001b[0m plain", state);

        Assert.Equal(AnsiColor.Indexed(1), Assert.Single(first.Spans!).Style.Foreground);
        var secondSpan = Assert.Single(second.Spans!);
        Assert.Equal(0, secondSpan.Start);
        Assert.Equal(6, secondSpan.Length);
        Assert.Equal(AnsiColor.Indexed(1), secondSpan.Style.Foreground);
        Assert.True(state.Style.IsDefault);
    }

    [Fact]
    public void DropsNonSgrTerminalControlSequences()
    {
        var state = new AnsiParserState();
        var parsed = AnsiLogParser.ParseLine("a\u001b]0;window-title\a b\u001b[2Jc", state);

        Assert.Equal("a bc", parsed.Text);
        Assert.Null(parsed.Spans);
    }

    [Fact]
    public void LargeVolumeParsingRemainsBoundedPerLine()
    {
        var state = new AnsiParserState();
        for (var index = 0; index < 50_000; index++)
        {
            var parsed = AnsiLogParser.ParseLine($"\u001b[38;2;10;20;30mINFO\u001b[0m line {index} 😀", state);
            Assert.StartsWith("INFO line ", parsed.Text, StringComparison.Ordinal);
            Assert.True(parsed.Spans is { Count: <= 1 });
        }
    }
}
