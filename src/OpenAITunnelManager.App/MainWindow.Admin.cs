using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenAITunnelManager.Infrastructure.TunnelClient;

namespace OpenAITunnelManager.App;

public sealed partial class MainWindow
{
    private AdminProfileInfo SelectedAdminProfile => AdminProfileCombo.SelectedItem as AdminProfileInfo
        ?? throw new InvalidOperationException("请选择 Admin Profile");
    private RemoteTunnelInfo SelectedRemoteTunnel => RemoteTunnelList.SelectedItem as RemoteTunnelInfo
        ?? throw new InvalidOperationException("请先查询并选择远端 Tunnel");
    private string AdminScope => (RemoteScopeType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Organization";
    private string AdminScopeId => RemoteScopeId.Text.Trim();
    private string? AdminSecret => string.IsNullOrEmpty(RemoteAdminSecret.Password) ? null : RemoteAdminSecret.Password;

    private void AdminProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RemoteTunnelList is null) return;
        RemoteTunnelList.ItemsSource = null;
        RemoteResultText.Text = string.Empty;
    }

    private async Task RunRemoteUiAsync(Func<Task> operation)
    {
        if (ViewModel.IsBusy || ViewModel.IsTunnelClientUpdating) return;
        try { await operation(); }
        catch (Exception exception) { RemoteStatusText.Text = exception.Message; }
    }

    private Task LoadAdminProfilesAsync() => RunRemoteUiAsync(async () =>
    {
        var selected = (AdminProfileCombo.SelectedItem as AdminProfileInfo)?.Name;
        var profiles = await ViewModel.ExecuteAdminAsync(service => service.ListProfilesAsync());
        AdminProfileCombo.ItemsSource = profiles;
        AdminProfileCombo.SelectedItem = profiles.FirstOrDefault(item => item.Name == selected)
            ?? profiles.FirstOrDefault(item => item.Active) ?? profiles.FirstOrDefault();
        RemoteStatusText.Text = $"已读取 {profiles.Count} 个 Admin Profile";
    });

    private async void RefreshAdminProfiles_Click(object sender, RoutedEventArgs e) => await LoadAdminProfilesAsync();

    private async void SaveAdminProfile_Click(object sender, RoutedEventArgs e) => await RunRemoteUiAsync(async () =>
    {
        var current = AdminProfileCombo.SelectedItem as AdminProfileInfo;
        var name = new TextBox { Header = "Profile 名称", Text = current?.Name ?? string.Empty };
        var endpoint = new TextBox { Header = "Control Plane Base URL", Text = current?.BaseUrl ?? "https://api.openai.com" };
        var path = new TextBox { Header = "Control Plane URL Path", Text = current?.UrlPath ?? string.Empty };
        var key = new TextBox { Header = "Admin Key 引用", Text = current?.KeyReference ?? "env:OPENAI_ADMIN_KEY" };
        var panel = new StackPanel { Spacing = 8 };
        foreach (var item in new[] { name, endpoint, path, key }) panel.Children.Add(item);
        if (await NewDialog("保存 Admin Profile", panel, "保存").ShowAsync() != ContentDialogResult.Primary) return;
        await ViewModel.ExecuteAdminAsync(service => service.SaveProfileAsync(name.Text.Trim(), endpoint.Text.Trim(), path.Text.Trim(), key.Text.Trim()));
        await LoadAdminProfilesAsync();
    });

    private async void ActivateAdminProfile_Click(object sender, RoutedEventArgs e) => await RunRemoteUiAsync(async () =>
    {
        await ViewModel.ExecuteAdminAsync(service => service.SetProfileStateAsync(SelectedAdminProfile.Name, false));
        await LoadAdminProfilesAsync();
    });

    private async void DeleteAdminProfile_Click(object sender, RoutedEventArgs e) => await RunRemoteUiAsync(async () =>
    {
        var profile = SelectedAdminProfile;
        if (await NewDialog("删除 Admin Profile", new TextBlock { Text = $"删除本机配置 {profile.Name}？" }, "删除").ShowAsync() != ContentDialogResult.Primary) return;
        await ViewModel.ExecuteAdminAsync(service => service.SetProfileStateAsync(profile.Name, true));
        await LoadAdminProfilesAsync();
    });

    private async Task ListRemoteAsync()
    {
        var profile = SelectedAdminProfile;
        ArgumentException.ThrowIfNullOrWhiteSpace(AdminScopeId);
        var items = await ViewModel.ExecuteAdminAsync(service => service.ListTunnelsAsync(profile, AdminScope, AdminScopeId, AdminSecret));
        RemoteTunnelList.ItemsSource = items;
        RemoteResultText.Text = string.Empty;
        RemoteStatusText.Text = $"已查询 {items.Count} 个远端 Tunnel";
    }

    private async void ListRemoteTunnels_Click(object sender, RoutedEventArgs e) => await RunRemoteUiAsync(ListRemoteAsync);
    private async void GetRemoteTunnel_Click(object sender, RoutedEventArgs e) => await RunRemoteUiAsync(async () =>
    {
        var result = await ViewModel.ExecuteAdminAsync(service => service.GetTunnelAsync(SelectedAdminProfile, SelectedRemoteTunnel.Id, AdminSecret));
        RemoteResultText.Text = result.Json;
    });

    private async void CreateRemoteTunnel_Click(object sender, RoutedEventArgs e) => await RunRemoteUiAsync(async () =>
    {
        var profile = SelectedAdminProfile;
        var scope = AdminScope;
        var scopeId = AdminScopeId;
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        var name = new TextBox { Header = "Tunnel 名称" };
        var description = new TextBox { Header = "描述" };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = $"{scope}: {scopeId}", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(name); panel.Children.Add(description);
        if (await NewDialog("创建远端 Tunnel", panel, "创建").ShowAsync() != ContentDialogResult.Primary) return;
        var result = await ViewModel.ExecuteAdminAsync(service => service.CreateTunnelAsync(profile, name.Text.Trim(), description.Text, scope, scopeId, AdminSecret));
        RemoteResultText.Text = result.Json;
        RemoteStatusText.Text = $"已创建 {result.Id}，服务端可能需要约 30 秒就绪";
        RemoteTunnelList.ItemsSource = null;
    });

    private async void UpdateRemoteTunnel_Click(object sender, RoutedEventArgs e) => await RunRemoteUiAsync(async () =>
    {
        var tunnel = SelectedRemoteTunnel;
        var profile = SelectedAdminProfile;
        var name = new TextBox { Header = "名称", Text = tunnel.Name };
        var description = new TextBox { Header = "描述", Text = tunnel.Description };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(name); panel.Children.Add(description);
        if (await NewDialog($"编辑 {tunnel.Id}", panel, "保存").ShowAsync() != ContentDialogResult.Primary) return;
        var result = await ViewModel.ExecuteAdminAsync(service => service.UpdateTunnelAsync(profile, tunnel.Id, name.Text.Trim(), description.Text, AdminSecret));
        RemoteResultText.Text = result.Json;
        RemoteStatusText.Text = $"已更新 {result.Id}，请重新查询列表";
        RemoteTunnelList.ItemsSource = null;
    });

    private async void DeleteRemoteTunnel_Click(object sender, RoutedEventArgs e) => await RunRemoteUiAsync(async () =>
    {
        var tunnel = SelectedRemoteTunnel;
        var profile = SelectedAdminProfile;
        var confirm = new TextBox { Header = $"请输入完整 Tunnel ID：{tunnel.Id}" };
        if (await NewDialog("永久删除远端 Tunnel", confirm, "删除").ShowAsync() != ContentDialogResult.Primary) return;
        if (confirm.Text.Trim() != tunnel.Id) throw new InvalidOperationException("Tunnel ID 不匹配，未执行删除");
        await ViewModel.ExecuteAdminAsync(service => service.DeleteTunnelAsync(profile, tunnel.Id, AdminSecret));
        RemoteTunnelList.ItemsSource = null;
        RemoteResultText.Text = string.Empty;
        RemoteStatusText.Text = $"已删除远端 Tunnel {tunnel.Id}，本机配置未删除";
    });

    private async void CleanupStaleRuntime_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var preview = await ViewModel.ExecuteAdminAsync(service => service.CleanupAsync(false));
            var box = new TextBox { Text = preview, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
            if (await NewDialog("官方 Runtime 清理预览", box, "清理过期记录").ShowAsync() != ContentDialogResult.Primary) return;
            if (ViewModel.Connections.Any(item => item.ProcessRunning && item.RunsInForeground))
                throw new InvalidOperationException("请先停止 Manager 持有的前台连接");
            var result = await ViewModel.ExecuteAdminAsync(service => service.CleanupAsync(true));
            await ViewModel.RefreshAsync();
            await NewDialog("清理结果", new TextBlock { Text = result, TextWrapping = TextWrapping.Wrap }, "确定").ShowAsync();
        }
        catch (Exception exception) { await ShowErrorAsync(exception.Message); }
    }
}
