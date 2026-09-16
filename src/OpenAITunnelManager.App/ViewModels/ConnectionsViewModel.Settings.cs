namespace OpenAITunnelManager.App.ViewModels;

public partial class ConnectionsViewModel
{
    public async Task PersistSettingsAsync()
    {
        _autostart.SetEnabled(StartWithWindows);

        Settings.TunnelClientPath = TunnelClientPath.Trim();
        Settings.CloseToTray = CloseToTray;
        Settings.StartWithWindows = StartWithWindows;
        Settings.ProfileDirectoryOverride = ProfileDirectoryOverride.Trim();
        Settings.StateDirectoryOverride = StateDirectoryOverride.Trim();

        await _settingsStore.SaveAsync(Settings);
        ApplySettingsToOptions();
        StatusMessage = "设置已应用";
        NotifyOverviewState();
    }

    public async Task ApplyTunnelClientPathAsync(string path)
    {
        var normalized = path.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || !File.Exists(normalized))
            throw new FileNotFoundException("选择的 tunnel-client.exe 不存在", normalized);

        TunnelClientPath = normalized;
        await PersistSettingsAsync();

        _initialAutoConnectApplied = false;
        await RefreshAsync();
        StartRuntimeEvents();
    }
}
