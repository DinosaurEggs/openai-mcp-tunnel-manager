namespace OpenAITunnelManager.App.ViewModels;

public partial class ConnectionsViewModel
{
    public async Task InitializeForManualRefreshAsync()
    {
        Settings = await _settingsStore.LoadAsync();
        TunnelClientPath = Settings.TunnelClientPath;
        ManagedTunnelClientVersion = Settings.ManagedTunnelClientVersion;
        CloseToTray = Settings.CloseToTray;
        StartWithWindows = _autostart.IsEnabled();
        ProfileDirectoryOverride = Settings.ProfileDirectoryOverride;
        StateDirectoryOverride = Settings.StateDirectoryOverride;
        ApplySettingsToOptions();
        NotifyTunnelClientSettingsChanged();

        if (!Settings.TunnelClientSetupCompleted)
        {
            SetClientUnavailable("首次启动需要配置 tunnel-client");
            return;
        }

        if (UsesManagedTunnelClient)
        {
            if (string.IsNullOrWhiteSpace(Settings.ManagedTunnelClientVersion) ||
                !_managedTunnelClient.IsInstalled(Settings.ManagedTunnelClientVersion))
            {
                SetClientUnavailable("托管 tunnel-client 尚未安装，请下载最新版或选择自定义版本");
                return;
            }
        }
        else if (string.IsNullOrWhiteSpace(TunnelClientPath))
        {
            SetClientUnavailable("请重新选择自定义 tunnel-client.exe");
            return;
        }

        // Runtime inventory/status is refreshed only on startup, manual refresh, and explicit
        // lifecycle/CRUD actions. No periodic runtime polling is started here.
        StartRuntimeEvents();
        await RefreshAsync();
    }

    private void SetClientUnavailable(string message)
    {
        IsClientAvailable = false;
        ClientVersion = "未检测";
        Connections.Clear();
        SelectedConnection = null;
        StatusMessage = message;
        NotifyOverviewState();
    }
}
