using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenAITunnelManager.App.Controls;
using OpenAITunnelManager.App.Diagnostics;

namespace OpenAITunnelManager.App;

public sealed partial class MainWindow
{
    private AnsiLogViewerControl? _ansiLogViewer;
    private bool _ansiLogAttachHooksConfigured;
    private bool _ansiLogFilterReady;

    private void InitializeAnsiLogViewer()
    {
        HideLogPathFooter();

        // TabView can defer non-selected content. Register a retry before checking the visual
        // parent so entering the log tab always gets another chance to attach the viewer.
        if (!_ansiLogAttachHooksConfigured)
        {
            _ansiLogAttachHooksConfigured = true;
            LogTextBox.Loaded += LegacyLogTextBox_Loaded;
            ConnectionTabs.SelectionChanged += EnsureAnsiLogViewerOnTabChanged;
        }

        if (_ansiLogViewer is not null) return;
        if (VisualTreeHelper.GetParent(LogTextBox) is not Border host) return;

        var viewer = new AnsiLogViewerControl
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        host.Child = viewer;
        _ansiLogViewer = viewer;
        _ansiLogFilterReady = false;
        viewer.SetWrap(ViewModel.LogWrap);

        // The ANSI viewer owns log file I/O. Keep the existing one-second timer, but detach the
        // legacy whole-string TextBox refresh path so large logs are never rebuilt every tick.
        _logTimer.Tick -= LogTimer_Tick;
        _logTimer.Tick += AnsiLogTimer_Tick;

        ConnectionsList.SelectionChanged += AnsiLogSelectionChanged;
        ConnectionTabs.SelectionChanged += AnsiLogTabChanged;
        ViewModel.PropertyChanged += ViewModel_LogViewerPropertyChanged;

        var refreshButton = LogsHeaderActions.Children
            .OfType<Button>()
            .FirstOrDefault(static button => string.Equals(button.Content as string, "立即刷新", StringComparison.Ordinal));
        if (refreshButton is not null)
        {
            refreshButton.Command = null;
            refreshButton.Click -= AnsiLogManualRefresh_Click;
            refreshButton.Click += AnsiLogManualRefresh_Click;
        }

        LogTextBox.Loaded -= LegacyLogTextBox_Loaded;

        if (IsLogTabSelected()) _ = RefreshAnsiLogAsync(forceReload: false);
    }

    private void LegacyLogTextBox_Loaded(object sender, RoutedEventArgs e) =>
        DispatcherQueue.TryEnqueue(InitializeAnsiLogViewer);

    private void EnsureAnsiLogViewerOnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLogTabSelected()) return;
        HideLogPathFooter();
        if (_ansiLogViewer is null) DispatcherQueue.TryEnqueue(InitializeAnsiLogViewer);
    }

    private void HideLogPathFooter()
    {
        if (VisualTreeHelper.GetParent(LogsHeader) is not Grid logRoot) return;

        foreach (var child in logRoot.Children.OfType<TextBlock>())
        {
            if (Grid.GetRow(child) == 6) child.Visibility = Visibility.Collapsed;
        }

        if (logRoot.RowDefinitions.Count > 5) logRoot.RowDefinitions[5].Height = new GridLength(0);
        if (logRoot.RowDefinitions.Count > 6) logRoot.RowDefinitions[6].Height = new GridLength(0);
    }

    private async void AnsiLogTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (!IsLogTabSelected() || !ViewModel.LogAutoRefresh || ViewModel.IsBusy) return;
        await RefreshAnsiLogAsync(forceReload: false);
    }

    private async void AnsiLogSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || !IsLogTabSelected()) return;
        _ansiLogFilterReady = false;
        await RefreshAnsiLogAsync(forceReload: false);
    }

    private async void AnsiLogTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || !IsLogTabSelected()) return;
        await RefreshAnsiLogAsync(forceReload: false);
    }

    private async void AnsiLogManualRefresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAnsiLogAsync(forceReload: true);
    }

    private void ViewModel_LogViewerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var viewer = _ansiLogViewer;
        if (viewer is null) return;
        if (e.PropertyName is nameof(ViewModel.LogSearch) or nameof(ViewModel.LogLevel))
        {
            // Do not schedule a filter rebuild before a cursor exists. The previous implementation
            // did this during startup; its delayed Clear() could run after a manual refresh and make
            // freshly rendered logs disappear about 200 ms later.
            if (_ansiLogFilterReady) viewer.SetFilter(ViewModel.LogSearch, ViewModel.LogLevel);
        }
        else if (e.PropertyName == nameof(ViewModel.LogWrap))
        {
            viewer.SetWrap(ViewModel.LogWrap);
        }
    }

    private async Task RefreshAnsiLogAsync(bool forceReload)
    {
        var viewer = _ansiLogViewer;
        if (viewer is null)
        {
            InitializeAnsiLogViewer();
            viewer = _ansiLogViewer;
            if (viewer is null) return;
        }

        var item = ViewModel.SelectedConnection;
        if (item is null)
        {
            _ansiLogFilterReady = false;
            viewer.Clear();
            return;
        }

        var path = !string.IsNullOrWhiteSpace(item.LogPath)
            ? item.LogPath
            : ViewModel.CurrentLogPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            _ansiLogFilterReady = false;
            viewer.Clear();
            return;
        }

        try
        {
            await viewer.ShowLogAsync(item.Identity, path, forceReload);

            // The viewer keeps the active query/level once configured and can append matching new
            // lines incrementally. Configure it only when a real cursor first becomes available;
            // rebuilding all visible lines every one-second refresh would defeat that design.
            if (!_ansiLogFilterReady && viewer.VisibleLines.Any(static line => line.Sequence >= 0))
            {
                _ansiLogFilterReady = true;
                viewer.SetFilter(ViewModel.LogSearch, ViewModel.LogLevel);
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("ANSI log viewer refresh failed", exception);
            ViewModel.StatusMessage = $"读取日志失败：{exception.Message}";
        }
    }
}
