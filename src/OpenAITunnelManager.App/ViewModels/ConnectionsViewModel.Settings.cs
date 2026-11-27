using OpenAITunnelManager.Core.Models;
using OpenAITunnelManager.Infrastructure.TunnelClient;

namespace OpenAITunnelManager.App.ViewModels;

public partial class ConnectionsViewModel
{
    private CancellationTokenSource? _tunnelClientOperationCts;

    public async Task PersistSettingsAsync()
    {
        var directoryChanged = !string.Equals(Settings.ProfileDirectoryOverride, ProfileDirectoryOverride.Trim(), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Settings.StateDirectoryOverride, StateDirectoryOverride.Trim(), StringComparison.OrdinalIgnoreCase);
        if (directoryChanged && (IsBusy || IsTunnelClientUpdating || Connections.Any(item => item.RunsInForeground && item.ProcessRunning)))
        {
            ProfileDirectoryOverride = Settings.ProfileDirectoryOverride;
            StateDirectoryOverride = Settings.StateDirectoryOverride;
            throw new InvalidOperationException("请先完成当前操作并停止 Manager 持有的前台连接，再切换目录");
        }
        if (directoryChanged && !Settings.LegacyConnectionDataMigrated)
            throw new InvalidOperationException("请先刷新原配置目录，完成历史偏好和凭据迁移");
        if (directoryChanged)
            foreach (var identity in _reconnectCancellations.Keys.ToArray()) CancelReconnect(identity, resetAttempts: true);

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
        if (directoryChanged)
        {
            Connections.Clear();
            SelectedConnection = null;
            _manualStopped.Clear();
            _lastLogPaths.Clear();
            _initialAutoConnectApplied = true;
            await RefreshAsync();
        }
    }

    public async Task SwitchToManagedTunnelClientAsync()
    {
        if (IsTunnelClientUpdating)
            throw new InvalidOperationException("当前正在执行 tunnel-client 操作");

        LastTunnelClientOperationFailed = false;
        TunnelClientUpdateStatus = string.Empty;
        Settings.TunnelClientSource = TunnelClientSource.Managed;
        Settings.TunnelClientSetupCompleted = true;
        await _settingsStore.SaveAsync(Settings);
        ApplySettingsToOptions();
        NotifyTunnelClientSettingsChanged();

        if (!string.IsNullOrWhiteSpace(Settings.ManagedTunnelClientVersion) &&
            _managedTunnelClient.IsInstalled(Settings.ManagedTunnelClientVersion))
        {
            _initialAutoConnectApplied = false;
            StartRuntimeEvents();
            await RefreshAsync();
            return;
        }

        SetClientUnavailable("托管 tunnel-client 尚未安装");
    }

    public async Task<TunnelClientValidationResult> ApplyTunnelClientPathAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (IsTunnelClientUpdating)
            throw new InvalidOperationException("当前正在执行 tunnel-client 操作");

        var validation = await _managedTunnelClient.ValidateCustomExecutableAsync(path, cancellationToken);

        LastTunnelClientOperationFailed = false;
        TunnelClientUpdateStatus = string.Empty;
        TunnelClientPath = validation.ExecutablePath;
        CustomTunnelClientVersionText = validation.VersionText;
        Settings.TunnelClientSource = TunnelClientSource.Custom;
        Settings.TunnelClientSetupCompleted = true;
        Settings.TunnelClientPath = validation.ExecutablePath;
        await _settingsStore.SaveAsync(Settings);
        ApplySettingsToOptions();
        NotifyTunnelClientSettingsChanged();

        _initialAutoConnectApplied = false;
        StartRuntimeEvents();
        await RefreshAsync();
        TunnelClientUpdateStatus = "自定义版本已就绪";
        NotifyTunnelClientSettingsChanged();
        return validation;
    }

    public async Task<ManagedTunnelClientInstallResult> InstallManagedTunnelClientAsync(
        bool forceDownload = false,
        CancellationToken cancellationToken = default)
    {
        if (IsTunnelClientUpdating)
            throw new InvalidOperationException("当前正在执行其他操作");

        var wasInstalled = !string.IsNullOrWhiteSpace(Settings.ManagedTunnelClientVersion) &&
                           _managedTunnelClient.IsInstalled(Settings.ManagedTunnelClientVersion);
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _tunnelClientOperationCts = linkedCts;

        IsTunnelClientUpdating = true;
        LastTunnelClientOperationFailed = false;
        TunnelClientOperationStage = "正在检查更新…";
        TunnelClientUpdateStatus = string.Empty;
        TunnelClientDownloadProgress = 0;
        TunnelClientDownloadIndeterminate = true;
        TunnelClientDownloadedBytes = 0;
        TunnelClientTotalBytes = null;
        CanCancelTunnelClientDownload = false;
        StatusMessage = wasInstalled
            ? "正在检查 OpenAI 官方 tunnel-client 更新..."
            : "正在获取 OpenAI 官方 tunnel-client...";

        var progress = new Progress<ManagedTunnelClientProgress>(UpdateTunnelClientProgress);

        try
        {
            var result = await _managedTunnelClient.InstallLatestAsync(
                Settings.ManagedTunnelClientVersion,
                forceDownload,
                linkedCts.Token,
                progress);

            Settings.TunnelClientSource = TunnelClientSource.Managed;
            Settings.TunnelClientSetupCompleted = true;
            Settings.ManagedTunnelClientVersion = result.Version;
            ManagedTunnelClientVersion = result.Version;
            await _settingsStore.SaveAsync(Settings);
            ApplySettingsToOptions();

            TunnelClientUpdateStatus = result.Message;
            StatusMessage = result.Message;
            NotifyTunnelClientSettingsChanged();

            _initialAutoConnectApplied = false;
            StartRuntimeEvents();
            await RefreshAsync();
            _managedTunnelClient.CleanupOldVersions(result.Version);
            return result;
        }
        catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
        {
            TunnelClientUpdateStatus = "操作已取消";
            StatusMessage = "tunnel-client 下载已取消";
            throw;
        }
        catch (Exception exception)
        {
            LastTunnelClientOperationFailed = true;
            TunnelClientUpdateStatus = $"{(wasInstalled ? "更新" : "下载")}失败：{exception.Message}";
            StatusMessage = TunnelClientUpdateStatus;
            throw;
        }
        finally
        {
            IsTunnelClientUpdating = false;
            CanCancelTunnelClientDownload = false;
            TunnelClientOperationStage = string.Empty;
            TunnelClientDownloadIndeterminate = false;
            _tunnelClientOperationCts = null;
            linkedCts.Dispose();
            NotifyTunnelClientSettingsChanged();
        }
    }

    public void CancelManagedTunnelClientDownload()
    {
        if (!CanCancelTunnelClientDownload) return;
        _tunnelClientOperationCts?.Cancel();
    }

    public int CleanupDownloadedTunnelClientVersions()
    {
        var deleted = _managedTunnelClient.CleanupDownloadedVersions(Settings.ManagedTunnelClientVersion);
        TunnelClientUpdateStatus = deleted == 0
            ? "没有可清理的旧版本"
            : $"已清理 {deleted} 个旧版本";
        StatusMessage = TunnelClientUpdateStatus;
        return deleted;
    }

    private void UpdateTunnelClientProgress(ManagedTunnelClientProgress progress)
    {
        TunnelClientOperationStage = progress.Message;
        CanCancelTunnelClientDownload = progress.Stage == ManagedTunnelClientProgressStage.Downloading;

        if (progress.Stage == ManagedTunnelClientProgressStage.Downloading)
        {
            TunnelClientDownloadedBytes = progress.BytesReceived;
            TunnelClientTotalBytes = progress.TotalBytes;
            TunnelClientDownloadIndeterminate = progress.TotalBytes is null or <= 0;
            TunnelClientDownloadProgress = progress.TotalBytes is > 0
                ? Math.Clamp(progress.BytesReceived * 100d / progress.TotalBytes.Value, 0d, 100d)
                : 0d;
            return;
        }

        TunnelClientDownloadIndeterminate = true;
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
