using OpenAITunnelManager.Core.Models;
using OpenAITunnelManager.Infrastructure.TunnelClient;

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
        NotifyTunnelClientSettingsChanged();
    }

    public async Task ApplyTunnelClientPathAsync(string path)
    {
        var normalized = path.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || !File.Exists(normalized))
            throw new FileNotFoundException("选择的 tunnel-client.exe 不存在", normalized);

        TunnelClientPath = normalized;
        Settings.TunnelClientSource = TunnelClientSource.Custom;
        Settings.TunnelClientSetupCompleted = true;
        Settings.TunnelClientPath = normalized;
        await PersistSettingsAsync();

        _initialAutoConnectApplied = false;
        await RefreshAsync();
        StartRuntimeEvents();
        TunnelClientUpdateStatus = "自定义版本由用户维护";
        NotifyTunnelClientSettingsChanged();
    }

    public async Task<ManagedTunnelClientInstallResult> InstallManagedTunnelClientAsync(
        bool forceDownload = false,
        CancellationToken cancellationToken = default)
    {
        if (IsBusy) throw new InvalidOperationException("当前正在执行其他操作");

        ManagedTunnelClientInstallResult result;
        IsBusy = true;
        StatusMessage = "正在检查 OpenAI 官方 tunnel-client 最新版本...";
        TunnelClientUpdateStatus = "正在检查更新...";
        try
        {
            result = await _managedTunnelClient.InstallLatestAsync(
                Settings.ManagedTunnelClientVersion,
                forceDownload,
                cancellationToken);

            Settings.TunnelClientSource = TunnelClientSource.Managed;
            Settings.TunnelClientSetupCompleted = true;
            Settings.ManagedTunnelClientVersion = result.Version;
            ManagedTunnelClientVersion = result.Version;
            await _settingsStore.SaveAsync(Settings);
            ApplySettingsToOptions();
            TunnelClientUpdateStatus = result.Message;
            StatusMessage = result.Message;
            NotifyTunnelClientSettingsChanged();
        }
        finally
        {
            IsBusy = false;
        }

        _initialAutoConnectApplied = false;
        StartRuntimeEvents();
        await RefreshAsync();
        _managedTunnelClient.CleanupOldVersions(result.Version);
        return result;
    }

    public async Task CheckForManagedTunnelClientUpdateAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Settings.TunnelClientSetupCompleted ||
            Settings.TunnelClientSource != TunnelClientSource.Managed ||
            IsBusy)
        {
            return;
        }

        ManagedTunnelClientInstallResult? result = null;
        IsBusy = true;
        TunnelClientUpdateStatus = "正在检查更新...";
        try
        {
            result = await _managedTunnelClient.InstallLatestAsync(
                Settings.ManagedTunnelClientVersion,
                forceDownload: false,
                cancellationToken);

            TunnelClientUpdateStatus = result.Message;
            if (!result.Updated)
            {
                NotifyTunnelClientSettingsChanged();
                return;
            }

            Settings.ManagedTunnelClientVersion = result.Version;
            ManagedTunnelClientVersion = result.Version;
            await _settingsStore.SaveAsync(Settings);
            ApplySettingsToOptions();
            StatusMessage = result.Message;
            NotifyTunnelClientSettingsChanged();
        }
        catch (Exception exception)
        {
            TunnelClientUpdateStatus = $"更新检查失败：{exception.Message}";
            if (!IsClientAvailable) StatusMessage = TunnelClientUpdateStatus;
            NotifyTunnelClientSettingsChanged();
            return;
        }
        finally
        {
            IsBusy = false;
        }

        if (result is not { Updated: true }) return;

        _initialAutoConnectApplied = false;
        StartRuntimeEvents();
        await RefreshAsync();
        _managedTunnelClient.CleanupOldVersions(result.Version);
    }

    private void NotifyTunnelClientSettingsChanged()
    {
        OnPropertyChanged(nameof(TunnelClientSetupCompleted));
        OnPropertyChanged(nameof(UsesManagedTunnelClient));
        OnPropertyChanged(nameof(UsesCustomTunnelClient));
        OnPropertyChanged(nameof(TunnelClientModeText));
        OnPropertyChanged(nameof(ManagedTunnelClientVersionText));
        OnPropertyChanged(nameof(ManagedTunnelClientPath));
    }
}
