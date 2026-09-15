using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OpenAITunnelManager.App;

public sealed partial class MainWindow
{
    private bool _uiPolishConfigured;

    private void ConfigureUiPolish()
    {
        if (_uiPolishConfigured) return;
        _uiPolishConfigured = true;
        ConfigureItemOnlyContextMenu();
        ConfigureAdvancedDirectoryPickers();
        RequestUiPolish();
    }

    private void RequestUiPolish() => DispatcherQueue.TryEnqueue(ApplyUiPolish);

    // Inventory polling is intentionally absent. Saving settings only ensures the
    // independent log-view timer is active; runtime status is owned by RuntimeMonitor.
    private void ResetTimers() => _logTimer.Start();

    private void ApplyUiPolish()
    {
        ConfigureAdvancedDirectoryPickers();

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

        NormalizeHeaderActions(ConnectionsHeaderActions);
        NormalizeHeaderActions(LogsHeaderActions);
        NormalizeHeaderActions(DiagnosticsHeaderActions);
    }

    private static void NormalizeHeaderActions(StackPanel panel)
    {
        panel.VerticalAlignment = VerticalAlignment.Center;
        panel.Spacing = 8;
        foreach (var child in panel.Children.OfType<FrameworkElement>())
        {
            child.VerticalAlignment = VerticalAlignment.Center;
        }
    }
}
