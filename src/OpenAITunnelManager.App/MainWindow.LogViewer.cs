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

    private void InitializeAnsiLogViewer()
    {
        if (_ansiLogViewer is not null) return;
        if (VisualTreeHelper.GetParent(LogTextBox) is not Border host) return;

        var viewer = new AnsiLogViewerControl
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        host.Child = viewer;
        _ansiLogViewer = viewer;
        viewer.SetFilter(ViewModel.LogSearch, ViewModel.LogLevel);
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
            refreshButton.Click += AnsiLogManualRefresh_Click;
        }
    }

    private async void AnsiLogTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (!IsLogTabSelected() || !ViewModel.LogAutoRefresh || ViewModel.IsBusy) return;
        await RefreshAnsiLogAsync(forceReload: false);
    }

    private async void AnsiLogSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || !IsLogTabSelected()) return;
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
            viewer.SetFilter(ViewModel.LogSearch, ViewModel.LogLevel);
        }
        else if (e.PropertyName == nameof(ViewModel.LogWrap))
        {
            viewer.SetWrap(ViewModel.LogWrap);
        }
    }

    private async Task RefreshAnsiLogAsync(bool forceReload)
    {
        var viewer = _ansiLogViewer;
        if (viewer is null) return;
        var item = ViewModel.SelectedConnection;
        if (item is null)
        {
            viewer.Clear();
            return;
        }

        var path = !string.IsNullOrWhiteSpace(item.LogPath)
            ? item.LogPath
            : ViewModel.CurrentLogPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            viewer.Clear();
            return;
        }

        try
        {
            await viewer.ShowLogAsync(item.Identity, path, forceReload);
        }
        catch (Exception exception)
        {
            AppLog.Error("ANSI log viewer refresh failed", exception);
            ViewModel.StatusMessage = $"读取日志失败：{exception.Message}";
        }
    }
}
