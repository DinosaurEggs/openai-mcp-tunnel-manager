using System.Buffers;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
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
    private FileIdentity? _identity;

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
        await using var stream = OpenLog(Path);
        var identity = GetIdentity(stream.SafeFileHandle);
        var length = stream.Length;
        var replaced = _identity is { } old && old != identity;
        var reset = forceReload || !_initialized || replaced || Offset > length;
        var backlog = reset ? length : length - Offset;
        var skipped = backlog > MaxBacklogBytes || (reset && length > InitialTailBytes);
        var start = reset || skipped ? Math.Max(0, length - InitialTailBytes) : Offset;
        var requested = checked((int)Math.Min(length - start, MaxReadPerRefreshBytes));
        var buffer = ArrayPool<byte>.Shared.Rent(Math.Max(requested, 1));
        try
        {
            stream.Seek(start, SeekOrigin.Begin);
            var total = 0;
            while (total < requested)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(total, requested - total), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                total += read;
            }
            cancellationToken.ThrowIfCancellationRequested();
            // Do not change the decoder/cursor until the entire read succeeded.
            if (reset || skipped) Reset();
            var lines = new List<string>();
            if (replaced && !forceReload) lines.Add("[Manager] 日志文件已轮转或替换");
            if (skipped) lines.Add("[Manager] 日志积压超出读取上限，已跳到最新内容");
            var offset = 0;
            if ((reset || skipped) && start > 0 && total > 0)
            {
                var newline = Array.IndexOf(buffer, (byte)'\n', 0, total);
                if (newline >= 0) offset = newline + 1;
                else
                {
                    offset = total;
                    _discardingLongLine = true;
                }
            }
            if (total > offset) DecodeBytes(buffer, offset, total - offset, lines);
            Offset = start + total;
            _initialized = true;
            _identity = identity;
            return lines;
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    public void SkipToEnd()
    {
        Reset();
        if (!File.Exists(Path)) return;
        using var stream = OpenLog(Path);
        _identity = GetIdentity(stream.SafeFileHandle);
        Offset = stream.Length;
        _initialized = true;
    }

    public void Reset()
    {
        Offset = 0;
        _decoder.Reset();
        _pending.Clear();
        _discardingLongLine = false;
        _initialized = false;
        _identity = null;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    private readonly record struct FileIdentity(uint Volume, ulong Index);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out NativeFileInformation information);

    private static FileIdentity GetIdentity(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var information))
            throw new IOException("无法读取日志文件标识", new Win32Exception(Marshal.GetLastWin32Error()));
        return new FileIdentity(information.VolumeSerialNumber,
            ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow);
    }
}
