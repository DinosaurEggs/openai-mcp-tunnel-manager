using OpenAITunnelManager.Infrastructure.TunnelClient;\n\nnamespace OpenAITunnelManager.App.ViewModels;

public partial class ConnectionsViewModel
{
    public async Task LoadSettingsAsync()
    {
        if (_settingsLoaded) return;

        Settings = await _settingsStore.LoadAsync();
        CloseToTray = Settings.CloseToTray;
        StartWithWindows = _autostart.IsEnabled();
        ProfileDirectoryOverride = Settings.ProfileDirectoryOverride;
        StateDirectoryOverride = Settings.StateDirectoryOverride;
        TunnelClientPath = string.Equals(Settings.TunnelClientSource, "custom", StringComparison.OrdinalIgnoreCase)
            ? Settings.TunnelClientPath
            : TunnelClientUpdateService.ManagedExecutablePath;
        _settingsLoaded = true;
        ApplySettingsToOptions();
    }

    public async Task InitializeForManualRefreshAsync()
    {
        await LoadSettingsAsync();

        if (!Settings.TunnelClientSetupCompleted)
        {
            IsClientAvailable = false;
            ClientVersion = "未检测";
            Connections.Clear();
            SelectedConnection = null;
            StatusMessage = "请选择下载最新版 tunnel-client，或使用自定义目录";
            NotifyOverviewState();
            return;
        }

        if (!string.Equals(Settings.TunnelClientSource, "custom", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var update = await _clientUpdateService.EnsureLatestAsync();
                TunnelClientPath = update.ExecutablePath;
                StatusMessage = update.Message;
            }
            catch (Exception exception)
            {
                if (!File.Exists(TunnelClientUpdateService.ManagedExecutablePath))
                {
                    IsClientAvailable = false;
                    ClientVersion = "未检测";
                    Connections.Clear();
                    SelectedConnection = null;
                    StatusMessage = $"下载 tunnel-client 失败：{exception.Message}";
                    NotifyOverviewState();
                    return;
                }

                StatusMessage = $"检查 tunnel-client 更新失败，继续使用已安装版本：{exception.Message}";
            }
        }

        ApplySettingsToOptions();

        // Runtime inventory/status is refreshed only on startup, manual refresh, and explicit
        // lifecycle/CRUD actions. No periodic runtime polling is started here.
        StartRuntimeEvents();
        await RefreshAsync();
    }
}
