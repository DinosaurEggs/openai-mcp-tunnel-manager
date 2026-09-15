using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace OpenAITunnelManager.App;

public sealed partial class MainWindow
{
    private bool _uiPolishConfigured;

    private void ConfigureUiPolish()
    {
        if (_uiPolishConfigured) return;
        _uiPolishConfigured = true;
        RootGrid.SizeChanged += UiPolish_SizeChanged;
        RequestUiPolish();
    }

    private void UiPolish_SizeChanged(object sender, SizeChangedEventArgs e) => RequestUiPolish();

    private void RequestUiPolish() => DispatcherQueue.TryEnqueue(ApplyUiPolish);

    // This remains as the settings-save hook used by MainWindow.Actions. Inventory polling
    // no longer exists; saving settings only ensures the independent log viewer timer runs.
    private void ResetTimers() => _logTimer.Start();

    private void ApplyUiPolish()
    {
        CacheResponsiveElements();
        CollapseObsoleteInventoryRefreshSetting();

        foreach (var button in FindDescendants<Button>(RootGrid))
        {
            button.VerticalAlignment = VerticalAlignment.Center;
            button.VerticalContentAlignment = VerticalAlignment.Center;
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.Padding = new Thickness(12, 6, 12, 6);

            if (button.Content is StackPanel contentPanel)
            {
                contentPanel.VerticalAlignment = VerticalAlignment.Center;
                foreach (var child in contentPanel.Children.OfType<FrameworkElement>())
                {
                    child.VerticalAlignment = VerticalAlignment.Center;
                }
            }
        }

        foreach (var toggle in FindDescendants<ToggleSwitch>(RootGrid))
        {
            toggle.VerticalAlignment = VerticalAlignment.Center;
        }

        foreach (var checkBox in FindDescendants<CheckBox>(RootGrid))
        {
            checkBox.VerticalAlignment = VerticalAlignment.Center;
        }

        NormalizeHeaderActions(_connectionsHeaderActions);
        NormalizeHeaderActions(_logsHeaderActions);
        NormalizeHeaderActions(_diagnosticsHeaderActions);

        var width = ConnectionsPage.ActualWidth > 1 ? ConnectionsPage.ActualWidth : RootGrid.ActualWidth;
        if (_connectionsPrimaryActions is not null)
        {
            // The details action row contains six buttons. At medium/narrow widths a single
            // horizontal row is what caused the visual offset/overflow. Stack the group and
            // let every button stretch with its parent instead of assigning fixed widths.
            var stacked = width < 1000;
            _connectionsPrimaryActions.Orientation = stacked ? Orientation.Vertical : Orientation.Horizontal;
            _connectionsPrimaryActions.HorizontalAlignment = stacked ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
            _connectionsPrimaryActions.VerticalAlignment = VerticalAlignment.Center;
            _connectionsPrimaryActions.Spacing = 8;

            foreach (var button in _connectionsPrimaryActions.Children.OfType<Button>())
            {
                button.HorizontalAlignment = stacked ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
            }
        }
    }

    private void CollapseObsoleteInventoryRefreshSetting()
    {
        if (_settingsRefreshGrid is null) return;
        _settingsRefreshGrid.Visibility = Visibility.Collapsed;

        if (VisualTreeHelper.GetParent(_settingsRefreshGrid) is not StackPanel parent) return;
        var index = -1;
        for (var i = 0; i < parent.Children.Count; i++)
        {
            if (!ReferenceEquals(parent.Children[i], _settingsRefreshGrid)) continue;
            index = i;
            break;
        }

        // Hide one adjacent separator as well so removing the refresh-frequency row does not
        // leave a double divider in Settings.
        if (index >= 0 && index + 1 < parent.Children.Count && parent.Children[index + 1] is Border separator)
        {
            separator.Visibility = Visibility.Collapsed;
        }
    }

    private static void NormalizeHeaderActions(StackPanel? panel)
    {
        if (panel is null) return;
        panel.VerticalAlignment = VerticalAlignment.Center;
        panel.Spacing = 8;
        foreach (var child in panel.Children.OfType<FrameworkElement>())
        {
            child.VerticalAlignment = VerticalAlignment.Center;
        }
    }
}
