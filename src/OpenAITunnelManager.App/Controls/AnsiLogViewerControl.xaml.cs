using System.Buffers;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenAITunnelManager.App.Collections;
using OpenAITunnelManager.Core.Logging;

namespace OpenAITunnelManager.App.Controls;

public sealed partial class AnsiLogViewerControl : UserControl, IDisposable
{
    private const int MaxCachedLines = 30_000;
    private const int TrimBatchLines = 2_000;
    private const int MaxCachedCharacters = 8 * 1024 * 1024;
    private const int MaxLineCharacters = 64 * 1024;
    private const int InitialTailBytes = 2 * 1024 * 1024;
    private const int MaxReadPerRefreshBytes = 2 * 1024 * 1024;
    private const int MaxBacklogBytes = 4 * 1024 * 1024;
    private const int ReadBufferBytes = 64 * 1024;
    private const int MaxCachedConnections = 8;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    private readonly Dictionary<string, LogCursor> _cursors = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _filterCancellation;
    private string _currentIdentity = string.Empty;
    private string _currentPath = string.Empty;
    private string _query = string.Empty;
    private string _level = "全部";
    private long _accessCounter;
    private bool _disposed;

    private sealed class LogCursor
    {
        public LogCursor(string path)
        {
            Path = path;
            Decoder = Utf8.GetDecoder();
        }

        public string Path { get; }
        public long Offset { get; set; }
        public Decoder Decoder { get; }
        public StringBuilder Pending { get; } = new();
        public AnsiParserState ParserState { get; } = new();
        public List<LogLine> Lines { get; } = [];
        public int CharacterCount { get; set; }
        public long NextSequence { get; set; }
        public long LastAccess { get; set; }
        public bool DiscardingLongLine { get; set; }
    }

    private readonly record struct RefreshResult(LogLine[] AddedLines, bool Trimmed);
    private readonly record struct ViewportState(double HorizontalOffset, double VerticalOffset, bool WasAtEnd);

    public AnsiLogViewerControl()
    {
        InitializeComponent();
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public BulkObservableCollection<LogLine> VisibleLines { get; } = [];

    public bool IsWrapped { get; private set; }

    public async Task ShowLogAsync(
        string identity,
        string path,
        bool forceReload = false,
        CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AnsiLogViewerControl));
        identity = identity?.Trim() ?? string.Empty;
        path = path?.Trim() ?? string.Empty;
        var switched = !string.Equals(_currentIdentity, identity, StringComparison.OrdinalIgnoreCase) ||
                       !PathsEqual(_currentPath, path);
        _currentIdentity = identity;
        _currentPath = path;

        if (string.IsNullOrWhiteSpace(identity) || string.IsNullOrWhiteSpace(path))
        {
            VisibleLines.Clear();
            return;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            ShowErrorLine($"读取日志失败：{exception.Message}");
            return;
        }
        _currentPath = fullPath;

        if (!File.Exists(fullPath))
        {
            ShowErrorLine($"读取日志失败：日志文件不存在：{fullPath}");
            return;
        }

        if (!await _refreshGate.WaitAsync(0, cancellationToken)) return;
        var viewport = CaptureViewport();
        try
        {
            var cursor = GetOrCreateCursor(identity, fullPath, forceReload);
            var result = await RefreshCursorAsync(cursor, forceReload, cancellationToken);
            cursor.LastAccess = ++_accessCounter;
            EvictOldCursors();

            if (!string.Equals(_currentIdentity, identity, StringComparison.OrdinalIgnoreCase) ||
                !PathsEqual(_currentPath, fullPath)) return;

            if (switched || forceReload || result.Trimmed || VisibleLines.Count == 0)
            {
                ApplyFilterNow(cursor);
            }
            else if (result.AddedLines.Length > 0)
            {
                var matches = result.AddedLines.Where(MatchesCurrentFilter).ToArray();
                if (matches.Length > 0) VisibleLines.AddRange(matches);
            }

            RestoreViewport(viewport);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ShowErrorLine($"读取日志失败：{exception.Message}");
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public Task RefreshCurrentAsync(bool forceReload = false, CancellationToken cancellationToken = default) =>
        ShowLogAsync(_currentIdentity, _currentPath, forceReload, cancellationToken);

    public void SetFilter(string? query, string? level)
    {
        _query = query?.Trim() ?? string.Empty;
        _level = string.IsNullOrWhiteSpace(level) ? "全部" : level.Trim();
        _filterCancellation?.Cancel();
        _filterCancellation?.Dispose();
        _filterCancellation = new CancellationTokenSource();
        _ = RebuildFilterAsync(_currentIdentity, _filterCancellation.Token);
    }

    public void SetWrap(bool wrap)
    {
        IsWrapped = wrap;
        AnsiLogLineControl.DefaultIsWrapped = wrap;
        ScrollViewer.SetHorizontalScrollMode(LinesList, wrap ? ScrollMode.Disabled : ScrollMode.Enabled);
        ScrollViewer.SetHorizontalScrollBarVisibility(LinesList, wrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto);
        ApplyWrapToRealizedControls(LinesList, wrap);
    }

    public void Clear()
    {
        _currentIdentity = string.Empty;
        _currentPath = string.Empty;
        VisibleLines.Clear();
    }

    private LogCursor GetOrCreateCursor(string identity, string path, bool forceReload)
    {
        if (forceReload ||
            !_cursors.TryGetValue(identity, out var cursor) ||
            !PathsEqual(cursor.Path, path))
        {
            cursor = new LogCursor(path);
            _cursors[identity] = cursor;
        }
        return cursor;
    }

    private async Task<RefreshResult> RefreshCursorAsync(LogCursor cursor, bool forceReload, CancellationToken cancellationToken)
    {
        var length = new FileInfo(cursor.Path).Length;
        if (forceReload || cursor.Offset > length)
        {
            ResetCursor(cursor);
            return await ReadTailAsync(cursor, length, cancellationToken);
        }

        if (cursor.Offset == 0 && cursor.Lines.Count == 0 && cursor.Pending.Length == 0)
        {
            return await ReadTailAsync(cursor, length, cancellationToken);
        }

        var backlog = length - cursor.Offset;
        if (backlog <= 0) return new RefreshResult([], false);
        if (backlog > MaxBacklogBytes)
        {
            ResetCursor(cursor);
            return await ReadTailAsync(cursor, length, cancellationToken);
        }

        var before = cursor.Lines.Count;
        await using var stream = OpenLog(cursor.Path);
        stream.Seek(cursor.Offset, SeekOrigin.Begin);
        var remaining = Math.Min(backlog, MaxReadPerRefreshBytes);
        var buffer = ArrayPool<byte>.Shared.Rent(ReadBufferBytes);
        try
        {
            while (remaining > 0)
            {
                var requested = (int)Math.Min(buffer.Length, remaining);
                var read = await stream.ReadAsync(buffer.AsMemory(0, requested), cancellationToken);
                if (read == 0) break;
                DecodeBytes(cursor, buffer, 0, read);
                cursor.Offset += read;
                remaining -= read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        var added = cursor.Lines.Count > before ? cursor.Lines.GetRange(before, cursor.Lines.Count - before).ToArray() : [];
        var trimmed = TrimCursor(cursor);
        return new RefreshResult(added, trimmed);
    }

    private async Task<RefreshResult> ReadTailAsync(LogCursor cursor, long length, CancellationToken cancellationToken)
    {
        if (length <= 0)
        {
            cursor.Offset = 0;
            return new RefreshResult([], false);
        }

        var start = Math.Max(0, length - InitialTailBytes);
        var bytesToRead = checked((int)(length - start));
        var buffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, bytesToRead));
        var totalRead = 0;
        try
        {
            await using var stream = OpenLog(cursor.Path);
            stream.Seek(start, SeekOrigin.Begin);
            while (totalRead < bytesToRead)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(totalRead, bytesToRead - totalRead), cancellationToken);
                if (read == 0) break;
                totalRead += read;
            }

            var offset = 0;
            if (start > 0 && totalRead > 0)
            {
                var newline = Array.IndexOf(buffer, (byte)'\n', 0, totalRead);
                offset = newline >= 0 ? newline + 1 : FindUtf8Boundary(buffer, totalRead);
            }

            if (totalRead > offset) DecodeBytes(cursor, buffer, offset, totalRead - offset);
            cursor.Offset = start + totalRead;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        var trimmed = TrimCursor(cursor);
        return new RefreshResult(cursor.Lines.ToArray(), trimmed);
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

    private static void DecodeBytes(LogCursor cursor, byte[] buffer, int offset, int count)
    {
        if (count <= 0) return;
        var charBuffer = ArrayPool<char>.Shared.Rent(Utf8.GetMaxCharCount(count));
        try
        {
            cursor.Decoder.Convert(
                buffer,
                offset,
                count,
                charBuffer,
                0,
                charBuffer.Length,
                flush: false,
                out _,
                out var charsUsed,
                out _);
            ProcessDecodedCharacters(cursor, charBuffer.AsSpan(0, charsUsed));
        }
        finally
        {
            ArrayPool<char>.Shared.Return(charBuffer);
        }
    }

    private static void ProcessDecodedCharacters(LogCursor cursor, ReadOnlySpan<char> characters)
    {
        foreach (var current in characters)
        {
            if (current == '\n')
            {
                var raw = cursor.Pending.ToString();
                cursor.Pending.Clear();
                if (raw.EndsWith('\r')) raw = raw[..^1];
                if (cursor.DiscardingLongLine) raw += " … [line truncated]";
                AppendParsedLine(cursor, raw);
                if (cursor.DiscardingLongLine) cursor.ParserState.Reset();
                cursor.DiscardingLongLine = false;
                continue;
            }

            if (cursor.DiscardingLongLine) continue;
            if (cursor.Pending.Length >= MaxLineCharacters)
            {
                cursor.DiscardingLongLine = true;
                continue;
            }
            cursor.Pending.Append(current);
        }
    }

    private static void AppendParsedLine(LogCursor cursor, string raw)
    {
        var parsed = AnsiLogParser.ParseLine(raw, cursor.ParserState);
        var line = new LogLine(cursor.NextSequence++, parsed.Text, parsed.Severity, parsed.Spans);
        cursor.Lines.Add(line);
        cursor.CharacterCount += line.Text.Length;
    }

    private static void ResetCursor(LogCursor cursor)
    {
        cursor.Offset = 0;
        cursor.Decoder.Reset();
        cursor.Pending.Clear();
        cursor.ParserState.Reset();
        cursor.Lines.Clear();
        cursor.CharacterCount = 0;
        cursor.NextSequence = 0;
        cursor.DiscardingLongLine = false;
    }

    private static bool TrimCursor(LogCursor cursor)
    {
        if (cursor.Lines.Count <= MaxCachedLines && cursor.CharacterCount <= MaxCachedCharacters) return false;
        var removeCount = 0;
        var remainingCharacters = cursor.CharacterCount;
        var targetLineCount = Math.Max(0, MaxCachedLines - TrimBatchLines);
        var targetCharacters = Math.Max(0, MaxCachedCharacters - 512 * 1024);

        while (removeCount < cursor.Lines.Count &&
               (removeCount < TrimBatchLines ||
                cursor.Lines.Count - removeCount > targetLineCount ||
                remainingCharacters > targetCharacters))
        {
            remainingCharacters -= cursor.Lines[removeCount].Text.Length;
            removeCount++;
        }

        if (removeCount <= 0) return false;
        cursor.Lines.RemoveRange(0, removeCount);
        cursor.CharacterCount = Math.Max(0, remainingCharacters);
        return true;
    }

    private async Task RebuildFilterAsync(string identity, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(200, cancellationToken);
            if (!_cursors.TryGetValue(identity, out var cursor))
            {
                EnqueueUi(() => VisibleLines.Clear());
                return;
            }

            var query = _query;
            var level = _level;
            var snapshot = cursor.Lines.ToArray();
            var filtered = await Task.Run(
                () => snapshot.Where(line => MatchesFilter(line, query, level)).ToArray(),
                cancellationToken);
            if (cancellationToken.IsCancellationRequested ||
                !string.Equals(_currentIdentity, identity, StringComparison.OrdinalIgnoreCase)) return;
            EnqueueUi(() => VisibleLines.ReplaceWith(filtered));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void EnqueueUi(Action action)
    {
        if (_dispatcherQueue.HasThreadAccess) action();
        else _dispatcherQueue.TryEnqueue(() => action());
    }

    private void ApplyFilterNow(LogCursor cursor)
    {
        VisibleLines.ReplaceWith(cursor.Lines.Where(MatchesCurrentFilter));
    }

    private bool MatchesCurrentFilter(LogLine line) => MatchesFilter(line, _query, _level);

    private static bool MatchesFilter(LogLine line, string query, string level)
    {
        if (!string.IsNullOrWhiteSpace(query) &&
            !line.Text.Contains(query, StringComparison.OrdinalIgnoreCase)) return false;
        return level switch
        {
            "TRACE" => line.Severity == LogSeverity.Trace,
            "DEBUG" => line.Severity == LogSeverity.Debug,
            "INFO" => line.Severity == LogSeverity.Info,
            "WARN" => line.Severity == LogSeverity.Warn,
            "ERROR" => line.Severity is LogSeverity.Error or LogSeverity.Fatal,
            _ => true
        };
    }

    private void EvictOldCursors()
    {
        if (_cursors.Count <= MaxCachedConnections) return;
        var remove = _cursors
            .Where(pair => !string.Equals(pair.Key, _currentIdentity, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static pair => pair.Value.LastAccess)
            .FirstOrDefault();
        if (!string.IsNullOrEmpty(remove.Key)) _cursors.Remove(remove.Key);
    }

    private void ShowErrorLine(string message)
    {
        var style = new AnsiTextStyle(AnsiColor.Indexed(1), null, false, false, false, false);
        VisibleLines.ReplaceWith([
            new LogLine(-1, message, LogSeverity.Error, [new AnsiTextSpan(0, message.Length, style)])
        ]);
    }

    private ViewportState CaptureViewport()
    {
        var viewer = FindDescendant<ScrollViewer>(LinesList);
        if (viewer is null) return default;
        var atEnd = viewer.ScrollableHeight - viewer.VerticalOffset <= 2d;
        return new ViewportState(viewer.HorizontalOffset, viewer.VerticalOffset, atEnd);
    }

    private void RestoreViewport(ViewportState state)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (_disposed) return;
            var viewer = FindDescendant<ScrollViewer>(LinesList);
            if (viewer is null) return;
            if (state.WasAtEnd && VisibleLines.Count > 0)
            {
                LinesList.ScrollIntoView(VisibleLines[VisibleLines.Count - 1]);
                viewer.ChangeView(IsWrapped ? 0d : state.HorizontalOffset, viewer.ScrollableHeight, null, disableAnimation: true);
            }
            else
            {
                viewer.ChangeView(IsWrapped ? 0d : state.HorizontalOffset, state.VerticalOffset, null, disableAnimation: true);
            }
        });
    }

    private static void ApplyWrapToRealizedControls(DependencyObject root, bool wrap)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is AnsiLogLineControl lineControl) lineControl.IsWrapped = wrap;
            ApplyWrapToRealizedControls(child, wrap);
        }
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) return match;
            var nested = FindDescendant<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static bool PathsEqual(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _filterCancellation?.Cancel();
        _filterCancellation?.Dispose();
        _refreshGate.Dispose();
        _cursors.Clear();
        VisibleLines.Clear();
    }
}
