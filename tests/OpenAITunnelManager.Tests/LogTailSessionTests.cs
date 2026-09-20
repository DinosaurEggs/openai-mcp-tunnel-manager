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
        try
        {
            await File.WriteAllTextAsync(path, "one\n", new UTF8Encoding(false));
            var session = new LogTailSession(path);

            Assert.Equal(["one"], await session.ReadAsync());

            await File.AppendAllTextAsync(path, "two", new UTF8Encoding(false));
            Assert.Empty(await session.ReadAsync());

            await File.AppendAllTextAsync(path, "\nthree\n", new UTF8Encoding(false));
            Assert.Equal(["two", "three"], await session.ReadAsync());
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
        try
        {
            await File.WriteAllTextAsync(path, "old-1\nold-2\n", new UTF8Encoding(false));
            var session = new LogTailSession(path);

            session.SkipToEnd();
            Assert.Empty(await session.ReadAsync());

            await File.AppendAllTextAsync(path, "new-1\nnew-2\n", new UTF8Encoding(false));
            Assert.Equal(["new-1", "new-2"], await session.ReadAsync());
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
        try
        {
            await File.WriteAllTextAsync(path, "a-long-old-line\n", new UTF8Encoding(false));
            var session = new LogTailSession(path);
            Assert.Single(await session.ReadAsync());

            await File.WriteAllTextAsync(path, "new\n", new UTF8Encoding(false));
            Assert.Equal(["new"], await session.ReadAsync());
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
        try
        {
            await File.WriteAllBytesAsync(path, []);
            var session = new LogTailSession(path);
            Assert.Empty(await session.ReadAsync());

            var encoded = Encoding.UTF8.GetBytes("中");
            await AppendBytesAsync(path, encoded[..2]);
            Assert.Empty(await session.ReadAsync());

            await AppendBytesAsync(path, [encoded[2], (byte)'\n']);
            Assert.Equal(["中"], await session.ReadAsync());
        }
        finally
        {
            TryDelete(path);
        }
    }

    private static string CreateTempPath() =>
        Path.Combine(Path.GetTempPath(), $"openai-tunnel-log-{Guid.NewGuid():N}.log");

    private static async Task AppendBytesAsync(string path, byte[] bytes)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete,
            4096,
            FileOptions.Asynchronous);
        await stream.WriteAsync(bytes);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { }
    }
}
