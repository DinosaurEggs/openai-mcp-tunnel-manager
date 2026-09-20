using System.Buffers;
using System.Text;

namespace OpenAITunnelManager.Infrastructure.Logging;

public sealed class LogTailSession
{
    private const int InitialTailBytes = 2 * 1024 * 1024;
    private const int MaxReadPerRefreshBytes = 2 * 1024 * 1024;
    private const int MaxBacklogBytes = 4 * 1024 * 1024;
    private const int ReadBufferBytes = 64 * 1024;
    private const int MaxLineCharacters = 64 * 1024;

    private static readonly UTF8Encoding Utf8 = new(false, false);
    private readonly Decoder _decoder = Utf8.GetDecoder();
    private readonly StringBuilder _pending = new();
    private bool _discardingLongLine;
    private bool _initialized;

    public LogTailSession(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
    }

    public string Path { get; }
    public long Offset { get; private set; }

    public async Task<IReadOnlyList<string>> ReadAsync(
        bool forceReload = false,
        CancellationToken cancellationToken = default)
    {
        var length = new FileInfo(Path).Length;
        if (forceReload || Offset > length)
        {
            Reset();
            return await ReadTailAsync(length, cancellationToken).ConfigureAwait(false);
        }

        if (!_initialized)
            return await ReadTailAsync(length, cancellationToken).ConfigureAwait(false);

        var backlog = length - Offset;
        if (backlog <= 0) return [];

        if (backlog > MaxBacklogBytes)
        {
            Reset();
            return await ReadTailAsync(length, cancellationToken).ConfigureAwait(false);
        }

        var lines = new List<string>();
        await using var stream = OpenLog(Path);
        stream.Seek(Offset, SeekOrigin.Begin);
        var remaining = Math.Min(backlog, MaxReadPerRefreshBytes);
        var buffer = ArrayPool<byte>.Shared.Rent(ReadBufferBytes);
        try
        {
            while (remaining > 0)
            {
                var requested = (int)Math.Min(buffer.Length, remaining);
                var read = await stream.ReadAsync(buffer.AsMemory(0, requested), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                DecodeBytes(buffer, 0, read, lines);
                Offset += read;
                remaining -= read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return lines;
    }

    public void SkipToEnd()
    {
        var length = File.Exists(Path) ? new FileInfo(Path).Length : 0;
        Offset = length;
        _decoder.Reset();
        _pending.Clear();
        _discardingLongLine = false;
        _initialized = true;
    }

    public void Reset()
    {
        Offset = 0;
        _decoder.Reset();
        _pending.Clear();
        _discardingLongLine = false;
        _initialized = false;
    }

    private async Task<IReadOnlyList<string>> ReadTailAsync(
        long length,
        CancellationToken cancellationToken)
    {
        _initialized = true;
        if (length <= 0)
        {
            Offset = 0;
            return [];
        }

        var start = Math.Max(0, length - InitialTailBytes);
        var bytesToRead = checked((int)(length - start));
        var buffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, bytesToRead));
        var totalRead = 0;
        try
        {
            await using var stream = OpenLog(Path);
            stream.Seek(start, SeekOrigin.Begin);
            while (totalRead < bytesToRead)
            {
                var read = await stream.ReadAsync(
                    buffer.AsMemory(totalRead, bytesToRead - totalRead),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                totalRead += read;
            }

            var offset = 0;
            if (start > 0 && totalRead > 0)
            {
                var newline = Array.IndexOf(buffer, (byte)'\n', 0, totalRead);
                offset = newline >= 0 ? newline + 1 : FindUtf8Boundary(buffer, totalRead);
            }

            var lines = new List<string>();
            if (totalRead > offset)
                DecodeBytes(buffer, offset, totalRead - offset, lines);

            Offset = start + totalRead;
            return lines;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void DecodeBytes(byte[] buffer, int offset, int count, ICollection<string> lines)
    {
        if (count <= 0) return;
        var charBuffer = ArrayPool<char>.Shared.Rent(Utf8.GetMaxCharCount(count));
        try
        {
            _decoder.Convert(
                buffer,
                offset,
                count,
                charBuffer,
                0,
                charBuffer.Length,
                false,
                out _,
                out var charsUsed,
                out _);
            ProcessCharacters(charBuffer.AsSpan(0, charsUsed), lines);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(charBuffer);
        }
    }

    private void ProcessCharacters(ReadOnlySpan<char> characters, ICollection<string> lines)
    {
        foreach (var current in characters)
        {
            if (current == '\n')
            {
                var raw = _pending.ToString();
                _pending.Clear();
                if (raw.EndsWith('\r')) raw = raw[..^1];
                if (_discardingLongLine) raw += " … [line truncated]";
                lines.Add(raw);
                _discardingLongLine = false;
                continue;
            }

            if (_discardingLongLine) continue;
            if (_pending.Length >= MaxLineCharacters)
            {
                _discardingLongLine = true;
                continue;
            }

            _pending.Append(current);
        }
    }

    private static FileStream OpenLog(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete,
        ReadBufferBytes,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static int FindUtf8Boundary(byte[] buffer, int count)
    {
        var limit = Math.Min(count, 4);
        for (var index = 0; index < limit; index++)
        {
            if ((buffer[index] & 0xC0) != 0x80) return index;
        }
        return limit;
    }
}
