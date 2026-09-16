using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OpenAITunnelManager.App;

public sealed partial class MainWindow
{
    private bool _settingsInteractionReady;

    private static string NormalizeThemeMode(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "light" => "light",
            "dark" => "dark",
            _ => "system"
        };

    private void InitializeThemeSetting()
    {
        _settingsInteractionReady = false;
        var mode = NormalizeThemeMode(ViewModel.Settings.ThemeMode);
        ViewModel.Settings.ThemeMode = mode;
        ThemeModeCombo.SelectedValue = mode;
        ApplyThemeMode(mode);
        _settingsInteractionReady = true;
    }

    private async void ThemeModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_settingsInteractionReady || sender is not ComboBox combo || combo.SelectedValue is not string selected) return;

        var mode = NormalizeThemeMode(selected);
        ViewModel.Settings.ThemeMode = mode;
        ApplyThemeMode(mode);
        await PersistImmediateSettingsAsync();
    }

    private async void ImmediateSettingToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_settingsInteractionReady || sender is not ToggleSwitch toggle || toggle.Tag is not string key) return;

        switch (key)
        {
            case "closeToTray":
                ViewModel.CloseToTray = toggle.IsOn;
                break;
            case "startWithWindows":
                ViewModel.StartWithWindows = toggle.IsOn;
                break;
            default:
                return;
        }

        await PersistImmediateSettingsAsync();
    }

    private async Task PersistImmediateSettingsAsync()
    {
        try
        {
            await ViewModel.PersistSettingsAsync();
        }
        catch (Exception exception)
        {
            await ShowErrorAsync($"应用设置失败：{exception.Message}");
        }
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
