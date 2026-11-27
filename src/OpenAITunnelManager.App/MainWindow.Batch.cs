using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OpenAITunnelManager.App;

public sealed partial class MainWindow
{
    private async void BatchConnections_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy || ViewModel.IsTunnelClientUpdating) return;
        var list = new ListView { ItemsSource = ViewModel.VisibleConnections.ToArray(), SelectionMode = ListViewSelectionMode.Multiple,
            DisplayMemberPath = "Name", MaxHeight = 320 };
        var action = new ComboBox { ItemsSource = new[] { "启动", "停止", "重启" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(action);
        panel.Children.Add(list);
        if (await NewDialog("批量操作", panel, "执行").ShowAsync() != ContentDialogResult.Primary ||
            list.SelectedItems.Count == 0 || action.SelectedItem is not string operation) return;
        try
        {
            var ids = list.SelectedItems.OfType<OpenAITunnelManager.Core.Models.TunnelConnection>().Select(item => item.Identity).ToArray();
            var results = await ViewModel.RunBatchAsync(operation, ids);
            await NewDialog("批量操作结果", new TextBlock { Text = string.Join(Environment.NewLine, results), TextWrapping = TextWrapping.Wrap }, "确定").ShowAsync();
        }
        catch (Exception exception) { await ShowErrorAsync(exception.Message); }
    }

    private async void CreateNativeRuntime_Click(object sender, RoutedEventArgs e)
    {
        var item = ViewModel.SelectedConnection;
        if (item is null || item.HasRuntime || item.ProcessRunning || !item.HasProfile)
        {
            await ShowErrorAsync("请选择已停止且未绑定 Runtime 的 Profile");
            return;
        }
        var alias = new TextBox { Header = "Runtime alias" };
        if (await NewDialog("创建并连接 Native Runtime", alias, "创建").ShowAsync() != ContentDialogResult.Primary) return;
        try { await ViewModel.CreateNativeRuntimeAsync(alias.Text); }
        catch (Exception exception) { await ShowErrorAsync(exception.Message); }
    }
}
