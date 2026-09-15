using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace OpenAITunnelManager.App;

public sealed partial class MainWindow
{
    private void ConfigureProfileTransferFlyout()
    {
        // Python 1.0.0's optimized UI intentionally keeps the configuration-list
        // context menu limited to Edit/Delete. Keep the transfer workflows available
        // for a future secondary command surface without changing that interaction.
    }

    private async Task ImportProfileAsync()
    {
        if (!ViewModel.IsClientAvailable)
        {
            SelectPage("settings");
            await ShowErrorAsync("请先在设置中选择可用的 tunnel-client.exe。");
            return;
        }

        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add(".yaml");
            picker.FileTypeFilter.Add(".yml");
            picker.FileTypeFilter.Add(".json");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            var name = new TextBox
            {
                Header = "Profile 名称",
                Text = Path.GetFileNameWithoutExtension(file.Path),
                PlaceholderText = "idea"
            };
            var hint = new TextBlock
            {
                Text = "导入内容由 tunnel-client 官方 profiles add 校验；Manager 不保存第二份 Profile。",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7
            };
            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(name);
            panel.Children.Add(hint);
            var dialog = NewDialog("导入 tunnel-client Profile", panel, "导入");
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            await ViewModel.ImportProfileAsync(name.Text, file.Path);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync($"导入 Profile 失败：{exception.Message}");
        }
    }

    private async Task ExportSelectedProfileAsync()
    {
        var item = ViewModel.SelectedConnection;
        if (item is null || !item.ProfileListed || !item.HasProfile)
        {
            await ShowErrorAsync("当前项目没有可导出的 profiles list Profile。");
            return;
        }

        try
        {
            var extension = Path.GetExtension(item.ProfilePath).ToLowerInvariant();
            var picker = new FileSavePicker
            {
                SuggestedFileName = item.ProfileName,
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };
            if (extension == ".json")
            {
                picker.FileTypeChoices.Add("JSON Profile", new List<string> { ".json" });
                picker.FileTypeChoices.Add("YAML Profile", new List<string> { ".yaml" });
            }
            else
            {
                picker.FileTypeChoices.Add("YAML Profile", new List<string> { ".yaml", ".yml" });
                picker.FileTypeChoices.Add("JSON Profile", new List<string> { ".json" });
            }
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;

            await ViewModel.ExportSelectedProfileAsync(file.Path);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync($"导出 Profile 失败：{exception.Message}");
        }
    }
}
