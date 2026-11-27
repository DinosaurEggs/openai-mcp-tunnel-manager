using OpenAITunnelManager.Core.Models;
using OpenAITunnelManager.Infrastructure.TunnelClient;
using OpenAITunnelManager.Infrastructure.Settings;

namespace OpenAITunnelManager.App.ViewModels;

public partial class ConnectionsViewModel
{
    private CancellationTokenSource? _tunnelClientOperationCts;
    private readonly SemaphoreSlim _clientSettingsGate = new(1, 1);

    public async Task PersistSettingsAsync()
    {
        await _clientSettingsGate.WaitAsync();
        try { await PersistSettingsCoreAsync(); }
        finally { _clientSettingsGate.Release(); }
    }

    private async Task PersistSettingsCoreAsync()
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

    private void EnsureClientChangeAllowed()
    {
        if (IsTunnelClientUpdating || IsBusy || _lifetimeCancellation.IsCancellationRequested ||
            Connections.Any(item => item.ProcessRunning && item.RunsInForeground))
            throw new InvalidOperationException("请先停止 Manager 持有的前台连接并完成其他操作");
    }

    private async Task CommitClientSelectionAsync(TunnelClientSource source, string path, string version, CancellationToken token)
    {
        await _clientSettingsGate.WaitAsync(token);
        try
        {
            var previous = (Settings.TunnelClientSource, Settings.TunnelClientSetupCompleted,
                Settings.TunnelClientPath, Settings.ManagedTunnelClientVersion);
            Settings.TunnelClientSource = source;
            Settings.TunnelClientSetupCompleted = true;
            Settings.TunnelClientPath = path;
            Settings.ManagedTunnelClientVersion = version;
            try { await _settingsStore.SaveAsync(Settings, token); }
            catch
            {
                (Settings.TunnelClientSource, Settings.TunnelClientSetupCompleted,
                    Settings.TunnelClientPath, Settings.ManagedTunnelClientVersion) = previous;
                throw;
            }
            TunnelClientPath = path;
            ManagedTunnelClientVersion = version;
            ApplySettingsToOptions();
            NotifyTunnelClientSettingsChanged();
        }
        finally { _clientSettingsGate.Release(); }
    }

    public async Task SwitchToManagedTunnelClientAsync()
    {
        EnsureClientChangeAllowed();
        var version = Settings.ManagedTunnelClientVersion;
        if (_managedTunnelClient.IsInstalled(version))
        {
            await UseInstalledTunnelClientVersionAsync(version);
            return;
        }

        IsTunnelClientUpdating = true;
        try
        {
            await CommitClientSelectionAsync(TunnelClientSource.Managed, Settings.TunnelClientPath,
                version, _lifetimeCancellation.Token);
            LastTunnelClientOperationFailed = false;
            TunnelClientUpdateStatus = string.Empty;
            SetClientUnavailable("托管 tunnel-client 尚未安装");
        }
        finally { IsTunnelClientUpdating = false; }
    }

    public async Task<TunnelClientValidationResult> ApplyTunnelClientPathAsync(
        string path, CancellationToken cancellationToken = default)
    {
        EnsureClientChangeAllowed();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCancellation.Token);
        IsTunnelClientUpdating = true;
        try
        {
            var validation = await _managedTunnelClient.ValidateCustomExecutableAsync(path, linked.Token);
            await ValidateProfilesForClientAsync(validation.ExecutablePath, linked.Token);
            await CommitClientSelectionAsync(TunnelClientSource.Custom, validation.ExecutablePath,
                Settings.ManagedTunnelClientVersion, linked.Token);
            LastTunnelClientOperationFailed = false;
            CustomTunnelClientVersionText = validation.VersionText;
            StartRuntimeEvents();
            await RefreshAsync();
            TunnelClientUpdateStatus = IsClientAvailable ? "自定义版本已就绪" : StatusMessage;
            return validation;
        }
        finally { IsTunnelClientUpdating = false; NotifyTunnelClientSettingsChanged(); }
    }

    public async Task<ManagedTunnelClientInstallResult> InstallManagedTunnelClientAsync(
        bool forceDownload = false,
        CancellationToken cancellationToken = default, string? requestedVersion = null)
    {
        EnsureClientChangeAllowed();

        var wasInstalled = !string.IsNullOrWhiteSpace(Settings.ManagedTunnelClientVersion) &&
                           _managedTunnelClient.IsInstalled(Settings.ManagedTunnelClientVersion);
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCancellation.Token);
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
            var result = await _managedTunnelClient.InstallVersionAsync(
                Settings.ManagedTunnelClientVersion,
                requestedVersion,
                forceDownload,
                linkedCts.Token,
                progress);

            await ValidateProfilesForClientAsync(result.ExecutablePath, linkedCts.Token);
            await CommitClientSelectionAsync(TunnelClientSource.Managed, Settings.TunnelClientPath,
                result.Version, linkedCts.Token);

            TunnelClientUpdateStatus = result.Message;
            StatusMessage = result.Message;
            NotifyTunnelClientSettingsChanged();

            StartRuntimeEvents();
            await RefreshAsync();
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

    public IReadOnlyList<string> GetInstalledTunnelClientVersions() => _managedTunnelClient.ListInstalledVersions();

    public async Task UseInstalledTunnelClientVersionAsync(string version)
    {
        EnsureClientChangeAllowed();
        IsTunnelClientUpdating = true;
        try
        {
            var path = _managedTunnelClient.GetExecutablePath(version);
            await _managedTunnelClient.ValidateInstalledVersionAsync(version, _lifetimeCancellation.Token);
            await ValidateProfilesForClientAsync(path, _lifetimeCancellation.Token);
            await CommitClientSelectionAsync(TunnelClientSource.Managed, Settings.TunnelClientPath,
                version, _lifetimeCancellation.Token);
            LastTunnelClientOperationFailed = false;
            StartRuntimeEvents();
            await RefreshAsync();
        }
        finally { IsTunnelClientUpdating = false; }
    }

    private async Task ValidateProfilesForClientAsync(string executablePath, CancellationToken token)
    {
        var candidate = new TunnelClientOptions
        {
            Source = TunnelClientSource.Custom, ExecutablePath = executablePath,
            ProfileDirectoryOverride = Settings.ProfileDirectoryOverride,
            StateDirectoryOverride = Settings.StateDirectoryOverride
        };
        var inventory = await new TunnelClientService(new TunnelClientProcessRunner(candidate)).GetConnectionsAsync(token);
        var files = inventory.Where(item => item.HasProfile).Select(item => item.ProfilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists).ToArray();
        if (files.Length == 0) return;
        var directory = Path.Combine(AppDataPaths.Current.TempDirectory, "validate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var runner = new TunnelClientProcessRunner(new TunnelClientOptions
            {
                Source = TunnelClientSource.Custom, ExecutablePath = executablePath,
                ProfileDirectoryOverride = directory, StateDirectoryOverride = directory
            });
            for (var index = 0; index < files.Length; index++)
            {
                var result = await runner.RunAsync(["profiles", "add", "check-" + index, "--from-file", files[index]], true, token);
                if (result.ExitCode != 0) throw new InvalidOperationException($"所选版本无法验证 Profile {Path.GetFileName(files[index])}：{result.StandardError}");
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    public int CleanupDownloadedTunnelClientVersions()
    {
        EnsureClientAvailable();
        if (Connections.Any(item => item.ProcessRunning || item.State == RuntimeState.Unknown))
            throw new InvalidOperationException("请先停止连接并刷新状态，再清理旧版本");
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
