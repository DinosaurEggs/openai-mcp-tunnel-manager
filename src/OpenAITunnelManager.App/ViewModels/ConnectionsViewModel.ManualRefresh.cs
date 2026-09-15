namespace OpenAITunnelManager.App.ViewModels;

public partial class ConnectionsViewModel
{
    public async Task InitializeForManualRefreshAsync()
    {
        Settings = await _settingsStore.LoadAsync();
        TunnelClientPath = Settings.TunnelClientPath;
        CloseToTray = Settings.CloseToTray;
        StartWithWindows = _autostart.IsEnabled();
        ProfileDirectoryOverride = Settings.ProfileDirectoryOverride;
        StateDirectoryOverride = Settings.StateDirectoryOverride;
        ApplySettingsToOptions();

        if (string.IsNullOrWhiteSpace(TunnelClientPath))
        {
            IsClientAvailable = false;
            ClientVersion = "未检测";
            Connections.Clear();
            SelectedConnection = null;
            StatusMessage = "请先在设置中选择 tunnel-client.exe";
            NotifyOverviewState();
            return;
        }

        // Inventory is loaded once at application entry. RuntimeMonitor subsequently checks
        // only known connection status; inventory is refreshed manually or after explicit CRUD/lifecycle actions.
        await RefreshAsync();
        StartRuntimeMonitor();
    }
}
