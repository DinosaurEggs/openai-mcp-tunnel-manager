using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OpenAITunnelManager.Core.Models;
using OpenAITunnelManager.Infrastructure.TunnelClient;
using Windows.Storage.Pickers;

namespace OpenAITunnelManager.App;

public sealed partial class MainWindow
{
    private async void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsClientAvailable)
        {
            SelectPage("settings");
            await ShowErrorAsync("请先在设置中选择可用的 tunnel-client.exe。");
            return;
        }

        var name = new TextBox { Header = "Profile 名称", PlaceholderText = "my-profile", HorizontalAlignment = HorizontalAlignment.Stretch };
        var tunnelId = new TextBox { Header = "Tunnel ID", PlaceholderText = "tunnel_ + 32 位小写十六进制字符", HorizontalAlignment = HorizontalAlignment.Stretch };
        var type = new ComboBox { Header = "MCP 类型", SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        type.Items.Add("HTTP URL");
        type.Items.Add("STDIO Command");
        var target = new TextBox { Header = "MCP 地址 / 命令", PlaceholderText = "例如 http://127.0.0.1:8000/mcp", HorizontalAlignment = HorizontalAlignment.Stretch };
        var secret = new PasswordBox { Header = "Runtime API Key", PlaceholderText = "可留空，填写后仅保存到 Windows 凭据管理器", HorizontalAlignment = HorizontalAlignment.Stretch };
        var enabled = new CheckBox { Content = "启用此配置", IsChecked = true };
        var autoConnect = new CheckBox { Content = "程序启动后自动连接" };
        var autoReconnect = new CheckBox { Content = "异常停止后自动重连" };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Microsoft.UI.Colors.IndianRed) };
        var panel = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var control in new UIElement[] { name, tunnelId, type, target, secret, enabled, autoConnect, autoReconnect, error }) panel.Children.Add(control);

        var dialog = NewDialog("新建 tunnel-client Profile", panel, "创建");
        ProfileSpec? spec = null;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            spec = new ProfileSpec(name.Text, tunnelId.Text, type.SelectedIndex == 1 ? McpType.Stdio : McpType.Http, target.Text);
            var errors = spec.Validate();
            if (errors.Count == 0) return;
            args.Cancel = true;
            error.Text = string.Join(Environment.NewLine, errors);
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary || spec is null) return;
        try
        {
            await ViewModel.CreateProfileAsync(spec, secret.Password, new ProfilePreference
            {
                Enabled = enabled.IsChecked == true,
                AutoConnect = autoConnect.IsChecked == true,
                AutoReconnect = autoReconnect.IsChecked == true
            });
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception.Message);
        }
    }

    private async void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanEditSelected)
        {
            await ShowErrorAsync("当前项目没有可编辑的 profiles list Profile。");
            return;
        }

        try
        {
            var data = await ViewModel.LoadSelectedProfileAsync();
            var commonTargetSupported = data.TargetKind is "server_url" or "command";
            var tunnelId = new TextBox { Header = "Tunnel ID", Text = data.TunnelId, HorizontalAlignment = HorizontalAlignment.Stretch };
            var target = new TextBox
            {
                Header = data.TargetKind == "command"
                    ? "main MCP Command"
                    : commonTargetSupported ? "main MCP URL" : $"main MCP target ({data.TargetKind}) - 请在高级配置中修改",
                Text = data.TargetValue,
                IsEnabled = commonTargetSupported,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var secret = new PasswordBox { Header = "新的 Runtime API Key", PlaceholderText = data.HasSavedSecret ? "已保存；留空表示不修改" : "未保存；留空使用环境变量", HorizontalAlignment = HorizontalAlignment.Stretch };
            var deleteSecret = new CheckBox { Content = "删除已保存的 Runtime API Key", IsEnabled = data.HasSavedSecret };
            var enabled = new CheckBox { Content = "启用此配置", IsChecked = data.Preference.Enabled };
            var autoConnect = new CheckBox { Content = "程序启动后自动连接", IsChecked = data.Preference.AutoConnect };
            var autoReconnect = new CheckBox { Content = "异常停止后自动重连", IsChecked = data.Preference.AutoReconnect };
            var raw = new TextBox
            {
                Header = "高级配置（完整 Profile YAML / JSON）",
                Text = data.Text,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new FontFamily("Cascadia Mono"),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            ConfigureProfileTextEditor(raw);
            ScrollViewer.SetHorizontalScrollBarVisibility(raw, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(raw, ScrollBarVisibility.Auto);

            var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Microsoft.UI.Colors.IndianRed) };
            var stack = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var control in new UIElement[] { tunnelId, target, enabled, autoConnect, autoReconnect, secret, deleteSecret, raw, error }) stack.Children.Add(control);
            var scroll = new ScrollViewer
            {
                Content = stack,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            var dialog = NewDialog($"编辑 Profile - {data.Name}", scroll, "保存");
            dialog.PrimaryButtonClick += (_, args) =>
            {
                var validationTarget = commonTargetSupported ? target.Text : "http://127.0.0.1/";
                var spec = new ProfileSpec(
                    data.Name,
                    tunnelId.Text,
                    data.TargetKind == "command" ? McpType.Stdio : McpType.Http,
                    validationTarget);
                var errors = spec.Validate();
                if (errors.Count == 0 && !string.IsNullOrWhiteSpace(raw.Text)) return;
                args.Cancel = true;
                error.Text = errors.Count > 0 ? string.Join(Environment.NewLine, errors) : "Profile 内容不能为空";
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            await ViewModel.SaveSelectedProfileAsync(
                data,
                raw.Text,
                tunnelId.Text,
                commonTargetSupported ? target.Text : data.TargetValue,
                new ProfilePreference
                {
                    Enabled = enabled.IsChecked == true,
                    AutoConnect = autoConnect.IsChecked == true,
                    AutoReconnect = autoReconnect.IsChecked == true
                },
                secret.Password,
                deleteSecret.IsChecked == true);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception.Message);
        }
    }

    private async void EditPreference_Click(object sender, RoutedEventArgs e)
    {
        var item = ViewModel.SelectedConnection;
        if (item is null) return;

        try
        {
            var preference = ViewModel.GetSelectedPreferenceCopy();
            var hasSecret = ViewModel.HasSavedSecret(item);
            var enabled = new CheckBox { Content = "启用此配置", IsChecked = preference.Enabled };
            var autoConnect = new CheckBox { Content = "程序启动后自动连接", IsChecked = preference.AutoConnect };
            var autoReconnect = new CheckBox { Content = "异常停止后自动重连", IsChecked = preference.AutoReconnect };
            var secret = new PasswordBox { Header = "新的 Runtime API Key", PlaceholderText = hasSecret ? "已保存；留空表示不修改" : "未保存；留空使用环境变量", HorizontalAlignment = HorizontalAlignment.Stretch };
            var deleteSecret = new CheckBox { Content = "删除已保存的 Runtime API Key", IsEnabled = hasSecret };
            var panel = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var control in new UIElement[] { enabled, autoConnect, autoReconnect, secret, deleteSecret }) panel.Children.Add(control);
            var dialog = NewDialog($"本机偏好 / 密钥 - {item.Name}", panel, "保存");
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            await ViewModel.SaveSelectedPreferenceAsync(
                new ProfilePreference
                {
                    Enabled = enabled.IsChecked == true,
                    AutoConnect = autoConnect.IsChecked == true,
                    AutoReconnect = autoReconnect.IsChecked == true
                },
                secret.Password,
                deleteSecret.IsChecked == true);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception.Message);
        }
    }

    private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var item = ViewModel.SelectedConnection;
        if (item is null) return;

        var profileLine = item.ProfileListed ? "\n• 如果没有其他 Runtime 共用该 Profile，则删除官方 Profile 文件" : string.Empty;
        var confirm = NewDialog(
            "删除 tunnel-client 配置",
            new TextBlock
            {
                Text = $"确定删除 {item.Name}？\n\n• 停止并移除对应 Runtime alias（如果有）{profileLine}\n• 删除对应本机偏好和不再使用的 Runtime API Key\n\n不会删除 OpenAI 平台上的远程 Tunnel。",
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Stretch
            },
            "删除");

        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            await ViewModel.DeleteSelectedAsync();
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception.Message);
        }
    }

    private void ConnectionsList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        var container = FindAncestor<ListViewItem>(source);
        if (container is not null) ConnectionsList.SelectedItem = container.Content;
    }

    private async void BrowseTunnelClient_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
            picker.FileTypeFilter.Add(".exe");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
            var file = await picker.PickSingleFileAsync();
            if (file is not null) ViewModel.SetTunnelClientPath(file.Path);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync($"选择 tunnel-client 失败：{exception.Message}");
        }
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.SaveSettingsAsync();
            ResetTimers();
            MissingClientInfo.IsOpen = !ViewModel.IsClientAvailable;
            if (ViewModel.IsClientAvailable) SelectPage("connections");
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception.Message);
        }
    }

    private void OpenConfigLocation_Click(object sender, RoutedEventArgs e) => OpenFileLocation(ViewModel.GetSelectedConfigPath());
    private void OpenLogLocation_Click(object sender, RoutedEventArgs e) => OpenFileLocation(ViewModel.CurrentLogPath);

    private static void OpenFileLocation(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Path.GetFullPath(path)}\"") { UseShellExecute = true });
    }

    private void LogWrap_Changed(object sender, RoutedEventArgs e)
    {
        var wrap = LogWrapCheckBox.IsChecked == true;
        LogTextBox.TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        ScrollViewer.SetHorizontalScrollBarVisibility(LogTextBox, wrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto);
    }

    private double CaptureLogHorizontalOffset() =>
        FindDescendant<ScrollViewer>(LogTextBox)?.HorizontalOffset ?? 0d;

    private void RestoreLogViewport(double horizontalOffset, bool followVertical)
    {
        var viewer = FindDescendant<ScrollViewer>(LogTextBox);
        if (viewer is null) return;
        var verticalOffset = followVertical && ViewModel.LogAutoRefresh
            ? viewer.ScrollableHeight
            : viewer.VerticalOffset;
        viewer.ChangeView(horizontalOffset, verticalOffset, null, disableAnimation: true);
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        DependencyObject? node = current;
        while (node is not null)
        {
            if (node is T match) return match;
            node = VisualTreeHelper.GetParent(node);
        }
        return null;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) return match;
            var nested = FindDescendant<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private ContentDialog NewDialog(string title, object content, string primaryText)
    {
        return new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = title,
            Content = PrepareDialogContent(content),
            PrimaryButtonText = primaryText,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
    }

    private async Task ShowErrorAsync(string message)
    {
        ViewModel.StatusMessage = message;
        var dialog = NewDialog("错误", new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch
        }, "确定");
        dialog.CloseButtonText = string.Empty;
        await dialog.ShowAsync();
    }
}
