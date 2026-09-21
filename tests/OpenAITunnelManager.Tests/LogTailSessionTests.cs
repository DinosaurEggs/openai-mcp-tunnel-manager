using System.Text;
using OpenAITunnelManager.Infrastructure.Logging;
using Xunit;

namespace OpenAITunnelManager.Tests;

public sealed class LogTailSessionTests
{
    [Fact]
    public async Task ReadsOnlyNewCompleteLines()
    {
        var path = CreateTempPath();
        var cancellationToken = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path, "one\n", new UTF8Encoding(false), cancellationToken);
            var session = new LogTailSession(path);

            Assert.Equal(["one"], await session.ReadAsync(cancellationToken: cancellationToken));

            await File.AppendAllTextAsync(path, "two", new UTF8Encoding(false), cancellationToken);
            Assert.Empty(await session.ReadAsync(cancellationToken: cancellationToken));

            await File.AppendAllTextAsync(path, "\nthree\n", new UTF8Encoding(false), cancellationToken);
            Assert.Equal(["two", "three"], await session.ReadAsync(cancellationToken: cancellationToken));
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task SkipToEndImplementsClearConsoleCursorSemantics()
    {
        var path = CreateTempPath();
        var cancellationToken = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path, "old-1\nold-2\n", new UTF8Encoding(false), cancellationToken);
            var session = new LogTailSession(path);

            session.SkipToEnd();
            Assert.Empty(await session.ReadAsync(cancellationToken: cancellationToken));

            await File.AppendAllTextAsync(path, "new-1\nnew-2\n", new UTF8Encoding(false), cancellationToken);
            Assert.Equal(["new-1", "new-2"], await session.ReadAsync(cancellationToken: cancellationToken));
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task DetectsFileTruncationAndStartsFromNewContent()
    {
        var path = CreateTempPath();
        var cancellationToken = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllTextAsync(path, "a-long-old-line\n", new UTF8Encoding(false), cancellationToken);
            var session = new LogTailSession(path);
            Assert.Single(await session.ReadAsync(cancellationToken: cancellationToken));

            await File.WriteAllTextAsync(path, "new\n", new UTF8Encoding(false), cancellationToken);
            Assert.Equal(["new"], await session.ReadAsync(cancellationToken: cancellationToken));
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task PreservesUtf8DecoderStateAcrossIncrementalReads()
    {
        var path = CreateTempPath();
        var cancellationToken = TestContext.Current.CancellationToken;
        try
        {
            await File.WriteAllBytesAsync(path, [], cancellationToken);
            var session = new LogTailSession(path);
            Assert.Empty(await session.ReadAsync(cancellationToken: cancellationToken));

            var encoded = Encoding.UTF8.GetBytes("中");
            await AppendBytesAsync(path, encoded[..2], cancellationToken);
            Assert.Empty(await session.ReadAsync(cancellationToken: cancellationToken));

            await AppendBytesAsync(path, [encoded[2], (byte)'\n'], cancellationToken);
            Assert.Equal(["中"], await session.ReadAsync(cancellationToken: cancellationToken));
        }
        finally
        {
            TryDelete(path);
        }
    }

    private static string CreateTempPath() =>
        Path.Combine(Path.GetTempPath(), $"openai-tunnel-log-{Guid.NewGuid():N}.log");

    private static async Task AppendBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete,
            4096,
            FileOptions.Asynchronous);
        await stream.WriteAsync(bytes, cancellationToken);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { }
    }
}
