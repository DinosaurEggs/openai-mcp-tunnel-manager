using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace OpenAITunnelManager.App;

public sealed partial class MainWindow
{
    private void ConfigureAdvancedDirectoryPickers()
    {
        // The settings page is now defined directly in XAML. Kept as a no-op so older
        // polish initialization remains harmless.
    }

    private async void BrowseDirectoryOverride_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string kind }) return;

        try
        {
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
            var folder = await picker.PickSingleFolderAsync();
            if (folder is null) return;

            if (string.Equals(kind, "profile", StringComparison.Ordinal))
                ViewModel.ProfileDirectoryOverride = folder.Path;
            else
                ViewModel.StateDirectoryOverride = folder.Path;
        }
        catch (Exception exception)
        {
            await ShowErrorAsync($"选择目录失败：{exception.Message}");
        }
    }

    private void ClearDirectoryOverride_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string kind }) return;
        if (string.Equals(kind, "profile", StringComparison.Ordinal))
            ViewModel.ProfileDirectoryOverride = string.Empty;
        else
            ViewModel.StateDirectoryOverride = string.Empty;
    }
}
