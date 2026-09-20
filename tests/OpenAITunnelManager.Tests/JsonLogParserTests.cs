using OpenAITunnelManager.Core.Logging;
using Xunit;

namespace OpenAITunnelManager.Tests;

public sealed class JsonLogParserTests
{
    [Theory]
    [InlineData("trace", LogSeverity.Trace)]
    [InlineData("DEBUG", LogSeverity.Debug)]
    [InlineData("Info", LogSeverity.Info)]
    [InlineData("warn", LogSeverity.Warn)]
    [InlineData("warning", LogSeverity.Warn)]
    [InlineData("error", LogSeverity.Error)]
    [InlineData("fatal", LogSeverity.Fatal)]
    [InlineData("critical", LogSeverity.Fatal)]
    public void ParsesLevelFieldOnly(string level, LogSeverity expected)
    {
        var parsed = JsonLogParser.ParseLine($$"""{"level":"{{level}}","message":"ERROR WARN INFO in message"}""");
        Assert.Equal(expected, parsed.Severity);
    }

    [Fact]
    public void MessageKeywordsDoNotAffectSeverity()
    {
        var parsed = JsonLogParser.ParseLine("""{"level":"info","message":"ERROR upstream returned WARN"}""");
        Assert.Equal(LogSeverity.Info, parsed.Severity);
        Assert.Equal("ERROR upstream returned WARN", parsed.Message);
    }

    [Fact]
    public void MissingLevelAndInvalidJsonAreUnknown()
    {
        Assert.Equal(LogSeverity.Unknown, JsonLogParser.ParseLine("""{"message":"ERROR"}""").Severity);
        Assert.Equal(LogSeverity.Unknown, JsonLogParser.ParseLine("ERROR plain text").Severity);
    }

    [Fact]
    public void ParsesTimestampLoggerAndUnicode()
    {
        var parsed = JsonLogParser.ParseLine(
            """{"timestamp":"2026-09-20T15:42:08.104Z","level":"warn","target":"runtime","message":"重连 😀"}""");

        Assert.Equal(LogSeverity.Warn, parsed.Severity);
        Assert.Equal("runtime", parsed.Logger);
        Assert.Equal("重连 😀", parsed.Message);
        Assert.NotNull(parsed.Timestamp);
        Assert.Contains("WARN", parsed.ToConsoleText(), StringComparison.Ordinal);
    }

    [Fact]
    public void ConsoleBufferFiltersAndFoldsConsecutiveDuplicates()
    {
        var buffer = new LogConsoleBuffer();
        buffer.Append([
            JsonLogParser.ParseLine("""{"level":"warn","message":"retry"}""", 1),
            JsonLogParser.ParseLine("""{"level":"warn","message":"retry"}""", 2),
            JsonLogParser.ParseLine("""{"level":"error","message":"failed"}""", 3)
        ]);

        var warn = buffer.SnapshotDisplay("WARN", foldDuplicates: true);
        var item = Assert.Single(warn);
        Assert.Equal(2, item.RepeatCount);
        Assert.Contains("×2", item.ToConsoleText(), StringComparison.Ordinal);

        var errors = buffer.Snapshot("ERROR");
        Assert.Single(errors);
        Assert.Equal(LogSeverity.Error, errors[0].Severity);
    }
}
