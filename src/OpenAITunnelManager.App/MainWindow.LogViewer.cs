using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenAITunnelManager.App.Diagnostics;

namespace OpenAITunnelManager.App;

public sealed partial class MainWindow
{
    private bool _ansiLogViewerConfigured;
    private bool _ansiLogFilterReady;

    private void ConfigureAnsiLogViewer()
    {
        if (_ansiLogViewerConfigured) return;
        _ansiLogViewerConfigured = true;
        _ansiLogFilterReady = false;

        // Keep the TabView and its content presenter constrained to the available details-panel
        // viewport. Without these alignments, the selected tab can be measured from the desired
        // height of its child content, which makes the log viewer shrink to the visible log rows.
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

        AnsiLogViewer.HorizontalAlignment = HorizontalAlignment.Stretch;
        AnsiLogViewer.VerticalAlignment = VerticalAlignment.Stretch;
        AnsiLogViewer.SetWrap(ViewModel.LogWrap);
        ViewModel.PropertyChanged += ViewModel_LogViewerPropertyChanged;
    }

    private async void AnsiLogManualRefresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAnsiLogAsync(forceReload: true);
    }

    private void ViewModel_LogViewerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModel.LogSearch) or nameof(ViewModel.LogLevel))
        {
            if (_ansiLogFilterReady)
            {
                AnsiLogViewer.SetFilter(ViewModel.LogSearch, ViewModel.LogLevel);
            }
        }
        else if (e.PropertyName == nameof(ViewModel.LogWrap))
        {
            AnsiLogViewer.SetWrap(ViewModel.LogWrap);
        }
    }

    private async Task RefreshAnsiLogAsync(bool forceReload, bool resetFilterState = false)
    {
        if (resetFilterState) _ansiLogFilterReady = false;

        var item = ViewModel.SelectedConnection;
        if (item is null)
        {
            _ansiLogFilterReady = false;
            AnsiLogViewer.Clear();
            return;
        }

        var path = !string.IsNullOrWhiteSpace(item.LogPath)
            ? item.LogPath
            : ViewModel.CurrentLogPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            _ansiLogFilterReady = false;
            AnsiLogViewer.Clear();
            return;
        }

        try
        {
            await AnsiLogViewer.ShowLogAsync(item.Identity, path, forceReload);

            // Configure the debounced filter only after a real file cursor exists. This prevents
            // a delayed pre-load filter task from clearing lines that were rendered by a refresh.
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch
            {
                fullPath = string.Empty;
            }

            if (!_ansiLogFilterReady && !string.IsNullOrEmpty(fullPath) && File.Exists(fullPath))
            {
                _ansiLogFilterReady = true;
                AnsiLogViewer.SetFilter(ViewModel.LogSearch, ViewModel.LogLevel);
            }
        }
        catch (Exception exception)
        {
            AppLog.Error("ANSI log viewer refresh failed", exception);
            ViewModel.StatusMessage = $"读取日志失败：{exception.Message}";
        }
    }

    private void DisposeAnsiLogViewer()
    {
        if (!_ansiLogViewerConfigured) return;
        _ansiLogViewerConfigured = false;
        ViewModel.PropertyChanged -= ViewModel_LogViewerPropertyChanged;
        AnsiLogViewer.Dispose();
    }
}
