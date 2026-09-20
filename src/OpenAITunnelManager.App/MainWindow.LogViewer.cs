using System.ComponentModel;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenAITunnelManager.App.Diagnostics;
using OpenAITunnelManager.Core.Logging;
using OpenAITunnelManager.Infrastructure.Logging;
using Windows.Storage.Pickers;

namespace OpenAITunnelManager.App;

public sealed partial class MainWindow
{
    private const int MaxCachedLogStates = 8;
    private readonly Dictionary<string, LogViewState> _logStates = new(StringComparer.OrdinalIgnoreCase);
    private bool _logConsoleConfigured;
    private bool _logPaused;
    private bool _logAtBottom = true;
    private int _logPendingLines;
    private int _logPausedLines;
    private long _logAccessCounter;
    private string _activeLogIdentity = string.Empty;

    private sealed class LogViewState
    {
        public LogViewState(string path)
        {
            Path = path;
            Tail = new LogTailSession(path);
        }

        public string Path { get; }
        public LogTailSession Tail { get; }
        public LogConsoleBuffer Buffer { get; } = new();
        public long NextSequence { get; set; }
        public long LastAccess { get; set; }
    }

    private void ConfigureLogConsole()
    {
        if (_logConsoleConfigured) return;
        _logConsoleConfigured = true;

        ConnectionTabs.HorizontalAlignment = HorizontalAlignment.Stretch;
        ConnectionTabs.VerticalAlignment = VerticalAlignment.Stretch;
        ConnectionTabs.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        ConnectionTabs.VerticalContentAlignment = VerticalAlignment.Stretch;

        foreach (var tab in ConnectionTabs.TabItems.OfType<TabViewItem>())
        {
            if (!string.Equals(tab.Header?.ToString(), "日志", StringComparison.Ordinal)) continue;
            tab.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            tab.VerticalContentAlignment = VerticalAlignment.Stretch;
            break;
        }

        LogConsole.HorizontalAlignment = HorizontalAlignment.Stretch;
        LogConsole.VerticalAlignment = VerticalAlignment.Stretch;
        LogConsole.BottomStateChanged += LogConsole_BottomStateChanged;
        LogConsole.Ready += LogConsole_Ready;
        ViewModel.PropertyChanged += ViewModel_LogConsolePropertyChanged;

        LogConsole.SetWrap(ViewModel.LogWrap);
        UpdateLogToolbarState();
    }

    private void LogConsole_Ready()
    {
        LogConsole.SetWrap(ViewModel.LogWrap);
        _ = ReplayCurrentLogAsync(followTail: true);
    }

    private void LogConsole_BottomStateChanged(bool atBottom)
    {
        _logAtBottom = atBottom;
        if (atBottom) _logPendingLines = 0;
        UpdateLogToolbarState();
    }

    private void ViewModel_LogConsolePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModel.LogLevel)
            or nameof(ViewModel.LogFoldDuplicates))
        {
            _ = ReplayCurrentLogAsync(_logAtBottom);
        }
        else if (e.PropertyName == nameof(ViewModel.LogWrap))
        {
            LogConsole.SetWrap(ViewModel.LogWrap);
        }
    }

    private async void LogManualRefresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshLogConsoleAsync(forceReload: false);
    }

    private async void LogPause_Click(object sender, RoutedEventArgs e)
    {
        _logPaused = !_logPaused;
        if (!_logPaused)
        {
            _logPausedLines = 0;
            await ReplayCurrentLogAsync(_logAtBottom);
        }
        UpdateLogToolbarState();
    }

    private void LogScrollToEnd_Click(object sender, RoutedEventArgs e)
    {
        _logAtBottom = true;
        _logPendingLines = 0;
        LogConsole.ScrollToEnd();
        UpdateLogToolbarState();
    }

    private void LogClear_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetCurrentLogState(out var state))
        {
            state.Buffer.Clear();
            state.Tail.SkipToEnd();
            state.NextSequence = 0;
        }

        _logPausedLines = 0;
        _logPendingLines = 0;
        _logAtBottom = true;
        LogConsole.Clear();
        UpdateLogToolbarState();
    }

    private void LogFindPrevious_Click(object sender, RoutedEventArgs e) =>
        LogConsole.FindPrevious(
            ViewModel.LogSearch,
            ViewModel.LogSearchRegex,
            ViewModel.LogSearchCaseSensitive);

    private void LogFindNext_Click(object sender, RoutedEventArgs e) =>
        LogConsole.FindNext(
            ViewModel.LogSearch,
            ViewModel.LogSearchRegex,
            ViewModel.LogSearchCaseSensitive);

    private void LogCopy_Click(object sender, RoutedEventArgs e) =>
        LogConsole.Copy();

    private void LogSelectAll_Click(object sender, RoutedEventArgs e) =>
        LogConsole.SelectAll();

    private async void LogSaveConsole_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetCurrentLogState(out var state)) return;

        try
        {
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = SafeFileName(ViewModel.SelectedConnection?.Name ?? "tunnel-client") + "-console"
            };
            picker.FileTypeChoices.Add("日志文件", new List<string> { ".log" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;

            var lines = state.Buffer
                .SnapshotDisplay(ViewModel.LogLevel, ViewModel.LogFoldDuplicates)
                .Select(static item => item.ToConsoleText());
            await File.WriteAllTextAsync(
                file.Path,
                string.Join(Environment.NewLine, lines),
                new UTF8Encoding(false));
            ViewModel.StatusMessage = $"控制台已保存：{file.Path}";
        }
        catch (Exception exception)
        {
            await ShowErrorAsync($"保存控制台失败：{exception.Message}");
        }
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var result = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(result) ? "tunnel-client" : result;
    }

    private async Task RefreshLogConsoleAsync(
        bool forceReload,
        bool resetConsoleState = false)
    {
        var item = ViewModel.SelectedConnection;
        if (item is null)
        {
            await ClearLogConsoleSelectionAsync();
            return;
        }

        var path = !string.IsNullOrWhiteSpace(item.LogPath)
            ? item.LogPath
            : ViewModel.CurrentLogPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            await ClearLogConsoleSelectionAsync();
            return;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception)
        {
            ViewModel.StatusMessage = $"读取日志失败：{exception.Message}";
            return;
        }

        if (!File.Exists(fullPath))
        {
            LogConsoleStatus.Text = $"等待日志文件：{fullPath}";
            return;
        }

        try
        {
            var switched = resetConsoleState ||
                           !string.Equals(_activeLogIdentity, item.Identity, StringComparison.OrdinalIgnoreCase) ||
                           !_logStates.TryGetValue(item.Identity, out var existing) ||
                           !PathsEqual(existing.Path, fullPath);

            var state = GetOrCreateLogState(item.Identity, fullPath, forceReload || resetConsoleState);
            _activeLogIdentity = item.Identity;
            state.LastAccess = ++_logAccessCounter;
            EvictOldLogStates();

            if (switched)
            {
                _logPaused = false;
                _logPausedLines = 0;
                _logPendingLines = 0;
                _logAtBottom = true;
            }

            if (forceReload)
            {
                state.Buffer.Clear();
                state.NextSequence = 0;
            }

            var rawLines = await state.Tail.ReadAsync(forceReload);
            var added = rawLines
                .Select(raw => JsonLogParser.ParseLine(raw, state.NextSequence++))
                .ToArray();
            if (added.Length > 0) state.Buffer.Append(added);

            if (switched || forceReload)
            {
                await ReplayStateAsync(state, followTail: true);
            }
            else if (added.Length > 0)
            {
                var visibleCount = added.Count(entry =>
                    LogConsoleBuffer.MatchesLevel(entry.Severity, ViewModel.LogLevel));

                if (_logPaused)
                {
                    _logPausedLines += visibleCount;
                }
                else if (ViewModel.LogFoldDuplicates)
                {
                    await ReplayStateAsync(state, _logAtBottom);
                }
                else
                {
                    var display = added
                        .Where(entry => LogConsoleBuffer.MatchesLevel(entry.Severity, ViewModel.LogLevel))
                        .Select(static entry => new LogDisplayEntry(entry, 1))
                        .ToArray();
                    LogConsole.Append(display, _logAtBottom);
                }

                if (!_logAtBottom) _logPendingLines += visibleCount;
            }

            UpdateLogToolbarState(state);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            AppLog.Error("Log console refresh failed", exception);
            ViewModel.StatusMessage = $"读取日志失败：{exception.Message}";
            LogConsoleStatus.Text = ViewModel.StatusMessage;
        }
    }

    private LogViewState GetOrCreateLogState(string identity, string path, bool reset)
    {
        if (reset ||
            !_logStates.TryGetValue(identity, out var state) ||
            !PathsEqual(state.Path, path))
        {
            state = new LogViewState(path);
            _logStates[identity] = state;
        }

        return state;
    }

    private async Task ReplayCurrentLogAsync(bool followTail)
    {
        if (!TryGetCurrentLogState(out var state)) return;
        await ReplayStateAsync(state, followTail);
        UpdateLogToolbarState(state);
    }

    private async Task ReplayStateAsync(LogViewState state, bool followTail)
    {
        if (_logPaused) return;
        var lines = state.Buffer
            .SnapshotDisplay(ViewModel.LogLevel, ViewModel.LogFoldDuplicates);
        LogConsole.ReplaceAll(lines, followTail);
        await Task.CompletedTask;
        if (followTail)
        {
            _logAtBottom = true;
            _logPendingLines = 0;
        }
    }

    private bool TryGetCurrentLogState(out LogViewState state)
    {
        if (!string.IsNullOrWhiteSpace(_activeLogIdentity) &&
            _logStates.TryGetValue(_activeLogIdentity, out var found))
        {
            state = found;
            return true;
        }

        state = null!;
        return false;
    }

    private async Task ClearLogConsoleSelectionAsync()
    {
        _activeLogIdentity = string.Empty;
        _logPaused = false;
        _logPausedLines = 0;
        _logPendingLines = 0;
        _logAtBottom = true;
        LogConsole.Clear();
        UpdateLogToolbarState();
        await Task.CompletedTask;
    }

    private void EvictOldLogStates()
    {
        while (_logStates.Count > MaxCachedLogStates)
        {
            var remove = _logStates
                .Where(pair => !string.Equals(pair.Key, _activeLogIdentity, StringComparison.OrdinalIgnoreCase))
                .OrderBy(static pair => pair.Value.LastAccess)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(remove.Key)) break;
            _logStates.Remove(remove.Key);
        }
    }

    private void UpdateLogToolbarState(LogViewState? state = null)
    {
        LogPauseButton.Content = _logPaused
            ? (_logPausedLines > 0 ? $"继续输出 ({_logPausedLines})" : "继续输出")
            : "暂停输出";
        LogScrollEndButton.Content = _logPendingLines > 0
            ? $"↓ {_logPendingLines} 条新日志"
            : "滚动到底部";

        if (state is null && TryGetCurrentLogState(out var current)) state = current;
        if (state is null)
        {
            LogConsoleStatus.Text = "未选择可读取的日志";
            return;
        }

        var trim = state.Buffer.WasTrimmed ? " · 较早内容已从控制台缓冲区移除" : string.Empty;
        var paused = _logPaused ? $" · 已暂停（{_logPausedLines} 条待显示）" : string.Empty;
        LogConsoleStatus.Text = $"{state.Buffer.Count:N0} 条缓存日志{paused}{trim}";
    }

    private void DisposeLogConsole()
    {
        if (!_logConsoleConfigured) return;
        _logConsoleConfigured = false;
        ViewModel.PropertyChanged -= ViewModel_LogConsolePropertyChanged;
        LogConsole.BottomStateChanged -= LogConsole_BottomStateChanged;
        LogConsole.Ready -= LogConsole_Ready;
        LogConsole.Dispose();
        _logStates.Clear();
    }
}
