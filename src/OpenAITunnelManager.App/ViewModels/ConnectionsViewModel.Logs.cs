using System.Text;
using CommunityToolkit.Mvvm.Input;

namespace OpenAITunnelManager.App.ViewModels;

public partial class ConnectionsViewModel
{
    private const int MaxCachedLogCharacters = 2 * 1024 * 1024;
    private readonly Dictionary<string, LogCursor> _logCursors = new(StringComparer.OrdinalIgnoreCase);

    private sealed record LogCursor(string Path, long Offset, string Text);

    public void RestoreSelectedLogCache()
    {
        var item = SelectedConnection;
        if (item is null)
        {
            CurrentLogPath = string.Empty;
            RawLog = string.Empty;
            VisibleLog = string.Empty;
            return;
        }

        if (_logCursors.TryGetValue(item.Identity, out var cached))
        {
            CurrentLogPath = cached.Path;
            RawLog = cached.Text;
            RenderLog();
            return;
        }

        CurrentLogPath = !string.IsNullOrWhiteSpace(item.LogPath)
            ? item.LogPath
            : _lastLogPaths.GetValueOrDefault(item.Identity, string.Empty);
        RawLog = string.Empty;
        VisibleLog = string.Empty;
    }

    [RelayCommand]
    public async Task RefreshLogIncrementalAsync(CancellationToken cancellationToken = default)
    {
        var item = SelectedConnection;
        if (item is null)
        {
            RestoreSelectedLogCache();
            return;
        }

        var path = !string.IsNullOrWhiteSpace(item.LogPath)
            ? item.LogPath
            : _lastLogPaths.GetValueOrDefault(item.Identity, string.Empty);

        if (string.IsNullOrWhiteSpace(path))
        {
            _logCursors.Remove(item.Identity);
            CurrentLogPath = string.Empty;
            RawLog = string.Empty;
            VisibleLog = string.Empty;
            return;
        }

        var fullPath = Path.GetFullPath(path);
        CurrentLogPath = fullPath;
        _lastLogPaths[item.Identity] = fullPath;

        if (!File.Exists(fullPath))
        {
            RawLog = string.Empty;
            VisibleLog = $"读取日志失败：日志文件不存在：{fullPath}";
            return;
        }

        try
        {
            var length = new FileInfo(fullPath).Length;
            if (!_logCursors.TryGetValue(item.Identity, out var cursor) ||
                !string.Equals(cursor.Path, fullPath, StringComparison.OrdinalIgnoreCase) ||
                cursor.Offset > length)
            {
                var tail = await _operations.ReadLogTailAsync(fullPath, maxBytes: 1024 * 1024, maxLines: 10000, cancellationToken);
                var initial = TrimLogCache(tail);
                _logCursors[item.Identity] = new LogCursor(fullPath, new FileInfo(fullPath).Length, initial);
                RawLog = initial;
                RenderLog();
                return;
            }

            if (length > cursor.Offset)
            {
                // If a very large burst was appended, re-tail instead of allocating the entire delta.
                if (length - cursor.Offset > 1024 * 1024)
                {
                    var tail = await _operations.ReadLogTailAsync(fullPath, maxBytes: 1024 * 1024, maxLines: 10000, cancellationToken);
                    var reset = TrimLogCache(tail);
                    _logCursors[item.Identity] = new LogCursor(fullPath, new FileInfo(fullPath).Length, reset);
                    RawLog = reset;
                    RenderLog();
                    return;
                }

                await using var stream = new FileStream(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                stream.Seek(cursor.Offset, SeekOrigin.Begin);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 64 * 1024, leaveOpen: true);
                var appended = await reader.ReadToEndAsync(cancellationToken);
                var nextOffset = stream.Position;
                var combined = TrimLogCache(cursor.Text + appended);
                cursor = new LogCursor(fullPath, nextOffset, combined);
                _logCursors[item.Identity] = cursor;
            }

            RawLog = cursor.Text;
            RenderLog();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            VisibleLog = $"读取日志失败：{exception.Message}";
        }
    }

    private static string TrimLogCache(string text)
    {
        if (text.Length <= MaxCachedLogCharacters) return text;
        var start = text.Length - MaxCachedLogCharacters;
        var newline = text.IndexOf('\n', start);
        return newline >= 0 && newline + 1 < text.Length ? text[(newline + 1)..] : text[start..];
    }
}
