using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OpenAITunnelManager.App;

public sealed partial class MainWindow
{
    private static string NormalizeThemeMode(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "light" => "light",
            "dark" => "dark",
            _ => "system"
        };

    private void InitializeThemeSetting()
    {
        var mode = NormalizeThemeMode(ViewModel.Settings.ThemeMode);
        ViewModel.Settings.ThemeMode = mode;
        ThemeModeCombo.SelectedValue = mode;
        ApplyThemeMode(mode);
    }

    private void ThemeModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo || combo.SelectedValue is not string selected) return;

        var mode = NormalizeThemeMode(selected);
        ViewModel.Settings.ThemeMode = mode;
        ApplyThemeMode(mode);
    }

    private void ApplyThemeMode(string? value)
    {
        RootGrid.RequestedTheme = NormalizeThemeMode(value) switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        RequestUiPolish();
        if (_profileModalOverlay?.Visibility == Visibility.Visible)
        {
            DispatcherQueue.TryEnqueue(UpdateProfileModalTheme);
        }
    }
}
