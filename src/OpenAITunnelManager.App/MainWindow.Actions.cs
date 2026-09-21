using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OpenAITunnelManager.App.Controls;
using OpenAITunnelManager.App.Diagnostics;
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

        var editor = new ProfileEditorControl();
        editor.InitializeForCreate();
        ProfileSpec? spec = null;

        bool ValidateCreate()
        {
            editor.ClearValidationError();
            spec = new ProfileSpec(editor.ProfileName, editor.TunnelId, editor.McpType, editor.TargetValue);
            var errors = spec.Validate();
            if (errors.Count > 0)
            {
                editor.ShowValidationError(string.Join(Environment.NewLine, errors));
                return false;
            }

            if (!editor.AdvancedEdited) return true;
            if (string.IsNullOrWhiteSpace(editor.RawText))
            {
                editor.ShowValidationError("Profile 内容不能为空", advanced: true);
                return false;
            }

            try
            {
                var metadata = ProfileDocumentEditor.ReadMetadata(editor.RawText);
                if (!string.Equals(metadata.TargetKind, editor.ExpectedTargetKind, StringComparison.Ordinal))
                {
                    editor.ShowValidationError(
                        $"高级配置中的 main MCP 类型为 {metadata.TargetKind}，与基本页选择的 {editor.ExpectedTargetKind} 不一致。",
                        advanced: true);
                    return false;
                }
            }
            catch (Exception exception)
            {
                editor.ShowValidationError(exception.Message, advanced: true);
                return false;
            }

            return true;
        }

        if (!await ShowProfileEditorDialogAsync("新建 Profile", editor, "创建", ValidateCreate) || spec is null) return;
        try
        {
            var preference = new ProfilePreference
            {
                Enabled = editor.Enabled,
                AutoConnect = editor.AutoConnect,
                AutoReconnect = editor.AutoReconnect
            };

            if (editor.AdvancedEdited)
            {
                await ViewModel.CreateProfileFromTextAsync(spec, editor.RawText, editor.Secret, preference);
            }
            else
            {
                await ViewModel.CreateProfileAsync(spec, editor.Secret, preference);
            }
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
            var editor = new ProfileEditorControl();
            editor.InitializeForEdit(
                data.Name,
                data.TunnelId,
                data.TargetKind,
                data.TargetValue,
                data.Preference.Enabled,
                data.Preference.AutoConnect,
                data.Preference.AutoReconnect,
                data.HasSavedSecret,
                data.Text);

            bool ValidateEdit()
            {
                editor.ClearValidationError();
                var validationTarget = editor.CommonTargetSupported ? editor.TargetValue : "http://127.0.0.1/";
                var spec = new ProfileSpec(
                    data.Name,
                    editor.TunnelId,
                    data.TargetKind == "command" ? McpType.Stdio : McpType.Http,
                    validationTarget);
                var errors = spec.Validate();
                if (errors.Count > 0)
                {
                    editor.ShowValidationError(string.Join(Environment.NewLine, errors));
                    return false;
                }

                if (string.IsNullOrWhiteSpace(editor.RawText))
                {
                    editor.ShowValidationError("Profile 内容不能为空", advanced: true);
                    return false;
                }

                try
                {
                    if (editor.CommonTargetSupported)
                    {
                        ProfileDocumentEditor.ApplyCommonFields(
                            editor.RawText,
                            data.TunnelId,
                            data.TargetKind,
                            data.TargetValue,
                            editor.TunnelId,
                            editor.TargetValue);
                    }
                    else
                    {
                        ProfileDocumentEditor.ReadMetadata(editor.RawText);
                    }
                }
                catch (Exception exception)
                {
                    editor.ShowValidationError(exception.Message, advanced: true);
                    return false;
                }

                return true;
            }

            if (!await ShowProfileEditorDialogAsync($"编辑 Profile - {data.Name}", editor, "保存", ValidateEdit)) return;
            await ViewModel.SaveSelectedProfileAsync(
                data,
                editor.RawText,
                editor.TunnelId,
                editor.CommonTargetSupported ? editor.TargetValue : data.TargetValue,
                new ProfilePreference
                {
                    Enabled = editor.Enabled,
                    AutoConnect = editor.AutoConnect,
                    AutoReconnect = editor.AutoReconnect
                },
                editor.Secret,
                editor.DeleteSecret);
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

    private bool _suppressTunnelClientSourceSelection;

    private async void TunnelClientPrimaryAction_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.UsesCustomTunnelClient)
        {
            await PickCustomTunnelClientAsync();
            RefreshTunnelClientSettingsUi();
            return;
        }

        if (ViewModel.IsTunnelClientUpdating)
        {
            if (ViewModel.CanCancelTunnelClientDownload)
                ViewModel.CancelManagedTunnelClientDownload();
            return;
        }

        try
        {
            await ViewModel.InstallManagedTunnelClientAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppLog.Error("Managed tunnel-client operation failed", exception);
        }
        finally
        {
            MissingClientInfo.IsOpen = !ViewModel.IsClientAvailable;
            RefreshTunnelClientSettingsUi();
        }
    }

    private async void TunnelClientSourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTunnelClientSourceSelection || !_initialized) return;
        if (TunnelClientSourceCombo.SelectedItem is not ComboBoxItem item ||
            item.Tag is not string source)
        {
            return;
        }

        if (string.Equals(source, "managed", StringComparison.OrdinalIgnoreCase))
        {
            if (ViewModel.UsesManagedTunnelClient) return;
            try
            {
                await ViewModel.SwitchToManagedTunnelClientAsync();
            }
            catch (Exception exception)
            {
                await ShowErrorAsync(exception.Message);
            }
            finally
            {
                MissingClientInfo.IsOpen = !ViewModel.IsClientAvailable;
                RefreshTunnelClientSettingsUi();
            }
            return;
        }

        if (ViewModel.UsesCustomTunnelClient) return;
        var selected = await PickCustomTunnelClientAsync();
        if (!selected) RefreshTunnelClientSettingsUi();
    }

    private async Task<bool> PickCustomTunnelClientAsync()
    {
        while (true)
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
            picker.FileTypeFilter.Add(".exe");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
            var file = await picker.PickSingleFileAsync();
            if (file is null) return false;

            try
            {
                await ViewModel.ApplyTunnelClientPathAsync(file.Path);
                MissingClientInfo.IsOpen = !ViewModel.IsClientAvailable;
                RefreshTunnelClientSettingsUi();
                return true;
            }
            catch (Exception exception)
            {
                AppLog.Error("Custom tunnel-client validation failed", exception);
                var dialog = NewDialog(
                    "无法使用所选程序",
                    new TextBlock
                    {
                        Text = $"所选程序未通过 tunnel-client 验证。\n\n{exception.Message}",
                        TextWrapping = TextWrapping.Wrap,
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    },
                    "重新选择");
                dialog.CloseButtonText = "取消";
                if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                {
                    RefreshTunnelClientSettingsUi();
                    return false;
                }
            }
        }
    }

    private async void CleanupManagedVersions_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.UsesManagedTunnelClient ||
            string.IsNullOrWhiteSpace(ViewModel.ManagedTunnelClientVersion))
        {
            return;
        }

        var dialog = NewDialog(
            "清理已下载版本",
            new TextBlock
            {
                Text = "将删除当前未使用的 tunnel-client 托管版本。正在使用的版本不会删除。",
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Stretch
            },
            "清理");

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        ViewModel.CleanupDownloadedTunnelClientVersions();
        RefreshTunnelClientSettingsUi();
    }

    private void OpenTunnelClientLocation_Click(object sender, RoutedEventArgs e)
    {
        var path = ViewModel.UsesManagedTunnelClient
            ? ViewModel.ManagedTunnelClientPath
            : ViewModel.TunnelClientPath;
        OpenFileLocation(path);
    }

    private async Task ShowFirstRunTunnelClientSetupAsync()
    {
        var managed = new RadioButton
        {
            Content = "自动管理官方版本",
            IsChecked = true,
            GroupName = "TunnelClientFirstRunSource"
        };
        var custom = new RadioButton
        {
            Content = "使用本地 tunnel-client.exe",
            GroupName = "TunnelClientFirstRunSource"
        };
        var description = new TextBlock
        {
            Text = "选择 tunnel-client 的来源。自动管理会下载 OpenAI 官方版本；本地版本会在选择后自动验证。",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75
        };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(description);
        panel.Children.Add(managed);
        panel.Children.Add(custom);

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "配置 tunnel-client",
            Content = PrepareDialogContent(panel),
            PrimaryButtonText = "下载",
            CloseButtonText = "稍后",
            DefaultButton = ContentDialogButton.Primary
        };

        managed.Checked += (_, _) => dialog.PrimaryButtonText = "下载";
        custom.Checked += (_, _) => dialog.PrimaryButtonText = "选择程序";

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        if (custom.IsChecked == true)
        {
            await PickCustomTunnelClientAsync();
            return;
        }

        try
        {
            await ViewModel.SwitchToManagedTunnelClientAsync();
            SelectPage("settings");
            RefreshTunnelClientSettingsUi();
            await ViewModel.InstallManagedTunnelClientAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppLog.Error("First-run managed tunnel-client download failed", exception);
        }
        finally
        {
            MissingClientInfo.IsOpen = !ViewModel.IsClientAvailable;
            RefreshTunnelClientSettingsUi();
        }
    }

    private void ViewModel_TunnelClientUiPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModel.ManagedTunnelClientVersion)
            or nameof(ViewModel.CustomTunnelClientVersionText)
            or nameof(ViewModel.ClientVersion)
            or nameof(ViewModel.IsClientAvailable)
            or nameof(ViewModel.IsTunnelClientUpdating)
            or nameof(ViewModel.TunnelClientOperationStage)
            or nameof(ViewModel.TunnelClientDownloadProgress)
            or nameof(ViewModel.TunnelClientDownloadIndeterminate)
            or nameof(ViewModel.TunnelClientDownloadedBytes)
            or nameof(ViewModel.TunnelClientTotalBytes)
            or nameof(ViewModel.CanCancelTunnelClientDownload)
            or nameof(ViewModel.LastTunnelClientOperationFailed)
            or nameof(ViewModel.TunnelClientUpdateStatus)
            or nameof(ViewModel.TunnelClientPath))
        {
            RefreshTunnelClientSettingsUi();
        }
    }

    private void RefreshTunnelClientSettingsUi()
    {
        if (TunnelClientSourceCombo is null) return;

        _suppressTunnelClientSourceSelection = true;
        try
        {
            TunnelClientSourceCombo.SelectedValue = ViewModel.UsesManagedTunnelClient ? "managed" : "custom";
        }
        finally
        {
            _suppressTunnelClientSourceSelection = false;
        }

        var managedInstalled = !string.IsNullOrWhiteSpace(ViewModel.ManagedTunnelClientVersion) &&
                               File.Exists(ViewModel.ManagedTunnelClientPath);

        if (ViewModel.UsesManagedTunnelClient)
        {
            TunnelClientSummaryText.Text = ViewModel.LastTunnelClientOperationFailed
                ? $"当前：托管版本 · {(managedInstalled ? ViewModel.ManagedTunnelClientVersion : "未安装")} · 操作失败"
                : managedInstalled
                    ? $"当前：托管版本 · {ViewModel.ManagedTunnelClientVersion} · 已就绪"
                    : "当前：托管版本 · 尚未安装";
            CleanupManagedVersionsMenuItem.Visibility = managedInstalled
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        else
        {
            var version = !string.IsNullOrWhiteSpace(ViewModel.CustomTunnelClientVersionText)
                ? ViewModel.CustomTunnelClientVersionText
                : ViewModel.ClientVersion;
            if (string.IsNullOrWhiteSpace(version) || string.Equals(version, "未检测", StringComparison.Ordinal))
                version = "已验证";
            TunnelClientSummaryText.Text = $"当前：自定义版本 · {version} · 已就绪";
            ToolTipService.SetToolTip(TunnelClientSummaryText, ViewModel.TunnelClientPath);
            CleanupManagedVersionsMenuItem.Visibility = Visibility.Collapsed;
        }

        TunnelClientSourceCombo.IsEnabled = !ViewModel.IsTunnelClientUpdating;
        TunnelClientMoreButton.IsEnabled = !ViewModel.IsTunnelClientUpdating;

        if (ViewModel.IsTunnelClientUpdating)
        {
            TunnelClientProgressPanel.Visibility = Visibility.Visible;
            TunnelClientOperationText.Text = ViewModel.TunnelClientOperationStage;
            TunnelClientDownloadProgressBar.IsIndeterminate = ViewModel.TunnelClientDownloadIndeterminate;
            TunnelClientDownloadProgressBar.Value = ViewModel.TunnelClientDownloadProgress;

            if (ViewModel.CanCancelTunnelClientDownload)
            {
                TunnelClientPrimaryActionButton.Content = "取消";
                TunnelClientPrimaryActionButton.IsEnabled = true;
            }
            else
            {
                TunnelClientPrimaryActionButton.Content =
                    ViewModel.TunnelClientOperationStage.Contains("检查", StringComparison.Ordinal)
                        ? "检查中"
                        : "处理中";
                TunnelClientPrimaryActionButton.IsEnabled = false;
            }

            TunnelClientTransferText.Text = ViewModel.TunnelClientDownloadedBytes > 0
                ? FormatTransferProgress(
                    ViewModel.TunnelClientDownloadedBytes,
                    ViewModel.TunnelClientTotalBytes)
                : string.Empty;
            return;
        }

        TunnelClientProgressPanel.Visibility = Visibility.Collapsed;
        TunnelClientPrimaryActionButton.IsEnabled = true;
        TunnelClientPrimaryActionButton.Content = ViewModel.UsesCustomTunnelClient
            ? "重新选择"
            : ViewModel.LastTunnelClientOperationFailed
                ? "重试"
                : managedInstalled ? "更新" : "下载";
        TunnelClientTransferText.Text = string.Empty;
    }

    private static string FormatTransferProgress(long received, long? total)
    {
        var receivedText = FormatBytes(received);
        return total is > 0
            ? $"{receivedText} / {FormatBytes(total.Value)}"
            : $"已下载 {receivedText}";
    }

    private static string FormatBytes(long value)
    {
        if (value >= 1024L * 1024L * 1024L) return $"{value / (1024d * 1024d * 1024d):0.0} GB";
        if (value >= 1024L * 1024L) return $"{value / (1024d * 1024d):0.0} MB";
        if (value >= 1024L) return $"{value / 1024d:0.0} KB";
        return $"{value} B";
    }

    private void OpenConfigLocation_Click(object sender, RoutedEventArgs e) => OpenFileLocation(ViewModel.GetSelectedConfigPath());
    private void OpenLogLocation_Click(object sender, RoutedEventArgs e) => OpenFileLocation(ViewModel.CurrentLogPath);

    private static void OpenFileLocation(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Path.GetFullPath(path)}\"") { UseShellExecute = true });
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
