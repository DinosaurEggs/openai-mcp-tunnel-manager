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
        ConfigureDashboardState();
        RequestUiPolish();
    }

    private void RequestUiPolish() => DispatcherQueue.TryEnqueue(ApplyUiPolish);

    // Runtime status polling is intentionally absent. The only periodic UI timer is
    // the log-view refresh timer, which remains active by design.
    private void ResetTimers() => _logTimer.Start();

    private void ApplyUiPolish()
    {
        ConfigureAdvancedDirectoryPickers();

        // Only normalize buttons owned by our page content. Do not mutate NavigationView
        // template buttons, otherwise the built-in pane toggle icon is pushed off-center.
        foreach (var button in FindDescendants<Button>(ContentGrid))
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

        NormalizeNavigationPaneToggle();

        foreach (var toggle in FindDescendants<ToggleSwitch>(ContentGrid))
            toggle.VerticalAlignment = VerticalAlignment.Center;

        foreach (var checkBox in FindDescendants<CheckBox>(ContentGrid))
            checkBox.VerticalAlignment = VerticalAlignment.Center;

        NormalizeHeaderActions(DashboardHeaderActions);
        NormalizeHeaderActions(ConnectionsHeaderActions);
        NormalizeHeaderActions(LogsHeaderActions);
        NormalizeHeaderActions(DiagnosticsHeaderActions);
    }

    private void NormalizeNavigationPaneToggle()
    {
        foreach (var button in FindDescendants<Button>(Navigation))
        {
            if (!button.Name.Contains("PaneToggle", StringComparison.OrdinalIgnoreCase) &&
                !button.Name.Contains("TogglePane", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            button.Padding = new Thickness(0);
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.VerticalContentAlignment = VerticalAlignment.Center;

            if (button.Content is FrameworkElement content)
            {
                content.HorizontalAlignment = HorizontalAlignment.Center;
                content.VerticalAlignment = VerticalAlignment.Center;
                content.Margin = new Thickness(0);
            }

            foreach (var icon in FindDescendants<FontIcon>(button))
            {
                icon.HorizontalAlignment = HorizontalAlignment.Center;
                icon.VerticalAlignment = VerticalAlignment.Center;
                icon.Margin = new Thickness(0);
            }
        }
    }

    private static void NormalizeHeaderActions(StackPanel panel)
    {
        panel.VerticalAlignment = VerticalAlignment.Center;
        panel.Spacing = 8;
        foreach (var child in panel.Children.OfType<FrameworkElement>())
            child.VerticalAlignment = VerticalAlignment.Center;
    }
}
