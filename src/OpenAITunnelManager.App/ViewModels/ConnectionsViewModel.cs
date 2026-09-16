using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenAITunnelManager.Core.Abstractions;
using OpenAITunnelManager.Core.Models;
using OpenAITunnelManager.Infrastructure.TunnelClient;

namespace OpenAITunnelManager.App.ViewModels;

public sealed record ProfileEditorData(
    string Name,
    string Path,
    string Text,
    string TunnelId,
    string TargetKind,
    string TargetValue,
    ProfilePreference Preference,
    bool HasSavedSecret);

public partial class ConnectionsViewModel : ObservableObject
{
    private readonly ITunnelClientService _inventory;
    private readonly ITunnelClientOperations _operations;
    private readonly ISettingsStore _settingsStore;
    private readonly ICredentialStore _credentials;
    private readonly IAutostartService _autostart;
    private readonly TunnelClientOptions _options;
    private readonly HashSet<string> _manualStopped = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _reconnectScheduled = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _reconnectAttempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _lastLogPaths = new(StringComparer.OrdinalIgnoreCase);
    private bool _initialAutoConnectApplied;

    public ConnectionsViewModel(
        ITunnelClientService inventory,
        ITunnelClientOperations operations,
        ISettingsStore settingsStore,
        ICredentialStore credentials,
        IAutostartService autostart,
        TunnelClientOptions options)
    {
        _inventory = inventory;
        _operations = operations;
        _settingsStore = settingsStore;
        _credentials = credentials;
        _autostart = autostart;
        _options = options;
    }

    public ObservableCollection<TunnelConnection> Connections { get; } = [];
    public AppSettings Settings { get; private set; } = new();

    [ObservableProperty] public partial TunnelConnection? SelectedConnection { get; set; }
    [ObservableProperty] public partial string ClientVersion { get; set; } = "未检测";
    [ObservableProperty] public partial string StatusMessage { get; set; } = "正在初始化...";
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial bool IsClientAvailable { get; set; }
    [ObservableProperty] public partial string TunnelClientPath { get; set; } = string.Empty;
    [ObservableProperty] public partial bool CloseToTray { get; set; } = true;
    [ObservableProperty] public partial bool StartWithWindows { get; set; }
    [ObservableProperty] public partial string ProfileDirectoryOverride { get; set; } = string.Empty;
    [ObservableProperty] public partial string StateDirectoryOverride { get; set; } = string.Empty;
    [ObservableProperty] public partial string LogSearch { get; set; } = string.Empty;
    [ObservableProperty] public partial string LogLevel { get; set; } = "全部";
    [ObservableProperty] public partial bool LogAutoRefresh { get; set; } = true;
    [ObservableProperty] public partial bool LogWrap { get; set; }
    [ObservableProperty] public partial string CurrentLogPath { get; set; } = string.Empty;
    [ObservableProperty] public partial string DiagnosticText { get; set; } = string.Empty;
    [ObservableProperty] public partial string HealthText { get; set; } = string.Empty;

    public bool CanStartSelected => IsClientAvailable && !IsBusy && SelectedConnection is { ProcessRunning: false } item && (item.HasRuntime || item.HasProfile);
    public bool CanStopSelected => IsClientAvailable && !IsBusy && SelectedConnection is { ProcessRunning: true };
    public bool CanRestartSelected => CanStopSelected;
    public bool CanEditSelected => IsClientAvailable && !IsBusy && SelectedConnection is { ProfileListed: true, HasProfile: true };
    public bool CanDeleteSelected => IsClientAvailable && !IsBusy && SelectedConnection is not null;
    public bool CanDiagnoseSelected => IsClientAvailable && !IsBusy && SelectedConnection is not null;
    public bool CanOpenConfigSelected => SelectedConnection is { HasProfile: true };
    public bool CanOpenLogSelected => !string.IsNullOrWhiteSpace(CurrentLogPath);
    public int TotalCount => Connections.Count;
    public int ActiveCount => Connections.Count(static item => item.ProcessRunning);
    public int HealthyCount => Connections.Count(static item => item.ProcessRunning && item.Healthy && item.Ready);
    public int ProblemCount => Connections.Count(static item => item.State is RuntimeState.Error or RuntimeState.Stale || (item.ProcessRunning && (!item.Healthy || !item.Ready)));

    partial void OnSelectedConnectionChanged(TunnelConnection? value)
    {
        if (value is not null && _lastLogPaths.TryGetValue(value.Identity, out var path)) CurrentLogPath = path;
        else CurrentLogPath = value?.LogPath ?? string.Empty;
        HealthText = string.Empty;
        NotifyActionState();
    }

    partial void OnIsBusyChanged(bool value) => NotifyActionState();
    partial void OnIsClientAvailableChanged(bool value) => NotifyActionState();
    partial void OnCurrentLogPathChanged(string value) => OnPropertyChanged(nameof(CanOpenLogSelected));

    public Task InitializeAsync() => InitializeForManualRefreshAsync();

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        var selectedIdentity = SelectedConnection?.Identity;
        StatusMessage = "正在从 tunnel-client 读取 Profile 和 Runtime...";
        try
        {
            _ = _operations.ResolveExecutablePath();
            IsClientAvailable = true;
            var capabilitiesTask = _operations.GetCapabilitiesAsync();
            var itemsTask = _inventory.GetConnectionsAsync();
            await Task.WhenAll(capabilitiesTask, itemsTask);
            ClientVersion = capabilitiesTask.Result.Version;

            var statuses = await ReadStatusesAsync(itemsTask.Result, CancellationToken.None);
            Connections.Clear();
            foreach (var item in statuses)
            {
                if (!string.IsNullOrWhiteSpace(item.LogPath)) _lastLogPaths[item.Identity] = item.LogPath;
                Connections.Add(item);
            }

            SelectedConnection = Connections.FirstOrDefault(item =>
                                     string.Equals(item.Identity, selectedIdentity, StringComparison.OrdinalIgnoreCase))
                                 ?? Connections.FirstOrDefault();
            StatusMessage = Connections.Count == 0
                ? "未发现 Profile 或 Runtime"
                : $"已从 tunnel-client 读取 {Connections.Count} 个配置/运行实例";
            NotifyOverviewState();
        }
        catch (Exception exception)
        {
            IsClientAvailable = false;
            ClientVersion = "未检测";
            StatusMessage = $"tunnel-client 不可用：{exception.Message}";
            DiagnosticText = StatusMessage;
        }
        finally
        {
            IsBusy = false;
        }

        if (!_initialAutoConnectApplied && IsClientAvailable)
        {
            _initialAutoConnectApplied = true;
            await ApplyAutoConnectAsync();
        }
    }

    private bool CanRefresh() => !IsBusy;

    public async Task RefreshSelectedStatusAsync()
    {
        var item = SelectedConnection;
        if (item is null || !IsClientAvailable || IsBusy) return;
        try
        {
            var status = await _operations.GetStatusAsync(item);
            ReplaceConnection(status);
            if (!string.IsNullOrWhiteSpace(status.LogPath))
            {
                _lastLogPaths[status.Identity] = status.LogPath;
                CurrentLogPath = status.LogPath;
            }
            EvaluateRuntimeReconnect(status);
            NotifyOverviewState();
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private void ApplySettingsToOptions() => _options.Apply(Settings);

    [RelayCommand(CanExecute = nameof(CanStart))]
    private Task StartSelectedAsync() => SelectedConnection is null ? Task.CompletedTask : StartItemAsync(SelectedConnection, manual: true);
    private bool CanStart() => CanStartSelected;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private Task StopSelectedAsync() => SelectedConnection is null ? Task.CompletedTask : StopItemAsync(SelectedConnection, manual: true);
    private bool CanStop() => CanStopSelected;

    [RelayCommand(CanExecute = nameof(CanRestart))]
    private async Task RestartSelectedAsync()
    {
        var item = SelectedConnection;
        if (item is null) return;
        _manualStopped.Remove(item.Identity);
        CancelReconnect(item.Identity, resetAttempts: true);
        IsBusy = true;
        StatusMessage = $"正在重启 {item.Name}...";
        try
        {
            var secret = ReadSavedSecret(item);
            await _operations.RestartAsync(item, secret);
            await RefreshAfterOperationAsync(item.Identity, $"{item.Name} 已重启");
        }
        catch (Exception exception)
        {
            StatusMessage = $"重启失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
    private bool CanRestart() => CanRestartSelected;

    public async Task CreateProfileAsync(ProfileSpec spec, string? secret, ProfilePreference preference)
    {
        EnsureClientAvailable();
        IsBusy = true;
        StatusMessage = $"正在创建 Profile：{spec.Name}";
        try
        {
            await _operations.CreateProfileAsync(spec);
            Settings.ProfilePreferences[$"profile:{spec.Name.Trim()}"] = preference;
            if (!string.IsNullOrWhiteSpace(secret)) _credentials.Set(spec.Name.Trim(), secret);
            await _settingsStore.SaveAsync(Settings);
            await RefreshAfterOperationAsync($"profile:{spec.Name.Trim()}", $"Profile 已创建：{spec.Name.Trim()}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<ProfileEditorData> LoadSelectedProfileAsync()
    {
        var item = SelectedConnection ?? throw new InvalidOperationException("请先选择一个配置");
        if (!item.ProfileListed || !item.HasProfile) throw new InvalidOperationException("当前项目没有可编辑的 profiles list Profile");
        var text = await _operations.ReadProfileTextAsync(item.ProfileName, item.ProfilePath);
        var metadata = ProfileDocumentEditor.ReadMetadata(text);
        return new ProfileEditorData(
            item.ProfileName,
            item.ProfilePath,
            text,
            metadata.TunnelId,
            metadata.TargetKind,
            metadata.TargetValue,
            GetSelectedPreferenceCopy(),
            HasSavedSecret(item));
    }

    public async Task SaveSelectedProfileAsync(
        ProfileEditorData original,
        string rawText,
        string tunnelId,
        string targetValue,
        ProfilePreference preference,
        string? newSecret,
        bool deleteSecret)
    {
        var item = SelectedConnection ?? throw new InvalidOperationException("请先选择一个配置");
        if (!string.Equals(item.ProfileName, original.Name, StringComparison.Ordinal) ||
            !string.Equals(item.ProfilePath, original.Path, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("当前选择的 Profile 已变化，请重新打开编辑器");
        }

        var updated = ProfileDocumentEditor.ApplyCommonFields(
            rawText,
            original.TunnelId,
            original.TargetKind,
            original.TargetValue,
            tunnelId.Trim(),
            targetValue.Trim());
        IsBusy = true;
        try
        {
            await _operations.SaveProfileTextAsync(item.ProfileName, item.ProfilePath, updated);
            await SavePreferenceForItemAsync(item, preference, newSecret, deleteSecret);
            await RefreshAfterOperationAsync(item.Identity, $"Profile 已保存：{item.ProfileName}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public ProfilePreference GetSelectedPreferenceCopy()
    {
        var item = SelectedConnection ?? throw new InvalidOperationException("请先选择一个配置");
        var pref = FindPreference(item) ?? new ProfilePreference();
        return new ProfilePreference { Enabled = pref.Enabled, AutoConnect = pref.AutoConnect, AutoReconnect = pref.AutoReconnect };
    }

    public Task SaveSelectedPreferenceAsync(ProfilePreference preference, string? newSecret = null, bool deleteSecret = false)
    {
        var item = SelectedConnection ?? throw new InvalidOperationException("请先选择一个配置");
        return SavePreferenceForItemAsync(item, preference, newSecret, deleteSecret);
    }

    private async Task SavePreferenceForItemAsync(TunnelConnection item, ProfilePreference preference, string? newSecret, bool deleteSecret)
    {
        Settings.ProfilePreferences[item.Identity] = preference;
        if (deleteSecret)
        {
            foreach (var id in CredentialIds(item)) _credentials.Delete(id);
        }
        else if (!string.IsNullOrWhiteSpace(newSecret))
        {
            _credentials.Set(CredentialWriteId(item), newSecret);
        }
        await _settingsStore.SaveAsync(Settings);
        if (!preference.Enabled || !preference.AutoReconnect)
        {
            CancelReconnect(item.Identity, resetAttempts: true);
        }
        else
        {
            EvaluateRuntimeReconnect(item);
        }
        StatusMessage = $"已保存 {item.Name} 的本机偏好";
    }

    public async Task DeleteSelectedAsync()
    {
        var original = SelectedConnection ?? throw new InvalidOperationException("请先选择一个配置");
        EnsureClientAvailable();
        IsBusy = true;
        try
        {
            var fresh = await _inventory.GetConnectionsAsync();
            var current = fresh.FirstOrDefault(item => string.Equals(item.Identity, original.Identity, StringComparison.OrdinalIgnoreCase));
            if (current is null && original.HasRuntime) throw new InvalidOperationException("列表已发生变化，请刷新后重试");
            var target = current ?? original;
            var sharedProfile = target.HasProfile && fresh.Any(item =>
                !string.Equals(item.Identity, target.Identity, StringComparison.OrdinalIgnoreCase) &&
                item.HasProfile &&
                PathsEqual(item.ProfilePath, target.ProfilePath));
            var profileDeleted = false;

            if (target.HasRuntime)
            {
                var status = await _operations.GetStatusAsync(target);
                if (status.ProcessRunning) await _operations.StopAsync(status);
                await _operations.RemoveRuntimeAsync(target.RuntimeAlias);
            }

            if (target.ProfileListed && target.HasProfile && !sharedProfile)
            {
                await _operations.DeleteProfileAsync(target.ProfileName, target.ProfilePath);
                profileDeleted = true;
            }

            if (profileDeleted)
            {
                foreach (var id in CredentialIds(original)) _credentials.Delete(id);
            }
            else if (original.HasRuntime)
            {
                _credentials.Delete(original.RuntimeAlias);
            }

            Settings.ProfilePreferences.Remove(original.Identity);
            Settings.ProfilePreferences.Remove(original.Name);
            if (profileDeleted && original.HasProfile)
            {
                Settings.ProfilePreferences.Remove($"profile:{original.ProfileName}");
                Settings.ProfilePreferences.Remove(original.ProfileName);
            }
            await _settingsStore.SaveAsync(Settings);
            _manualStopped.Remove(original.Identity);
            CancelReconnect(original.Identity, resetAttempts: true);
            await RefreshAfterOperationAsync(null, $"已删除：{original.Name}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDiagnose))]
    private async Task DiagnoseSelectedAsync()
    {
        var item = SelectedConnection;
        if (item is null) return;
        IsBusy = true;
        DiagnosticText = "正在运行 tunnel-client doctor --explain ...";
        try
        {
            DiagnosticText = await _operations.DoctorAsync(item, ReadSavedSecret(item));
            StatusMessage = $"{item.Name} 诊断完成";
        }
        catch (Exception exception)
        {
            DiagnosticText = $"诊断失败：{Environment.NewLine}{exception.Message}";
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
    private bool CanDiagnose() => CanDiagnoseSelected;

    [RelayCommand]
    public async Task RefreshHealthAsync()
    {
        var item = SelectedConnection;
        if (item is null) { HealthText = string.Empty; return; }
        try
        {
            HealthText = await _operations.GetDetailedHealthAsync(item);
        }
        catch (Exception exception)
        {
            HealthText = $"读取 Health 失败：{exception.Message}";
        }
    }

    public bool HasSavedSecret(TunnelConnection item) => ReadSavedSecret(item) is not null;

    public string? ReadSavedSecret(TunnelConnection item)
    {
        foreach (var id in CredentialIds(item))
        {
            var secret = _credentials.Get(id);
            if (!string.IsNullOrEmpty(secret)) return secret;
        }
        return null;
    }

    public string GetSelectedConfigPath()
    {
        var item = SelectedConnection;
        return item?.ProfilePath ?? item?.RuntimeProfilePath ?? string.Empty;
    }

    public async Task ShutdownAsync()
    {
        await StopRuntimeEventsAsync();
        await _operations.ShutdownForegroundProfilesAsync();
    }

    private async Task StartItemAsync(TunnelConnection item, bool manual)
    {
        EnsureClientAvailable();
        var pref = FindPreference(item) ?? new ProfilePreference();
        if (!pref.Enabled)
        {
            if (manual) StatusMessage = "此配置已在本机偏好中禁用";
            return;
        }

        _manualStopped.Remove(item.Identity);
        if (manual) CancelReconnect(item.Identity, resetAttempts: true);
        IsBusy = true;
        StatusMessage = $"正在启动 {item.Name}...";
        try
        {
            await _operations.StartAsync(item, ReadSavedSecret(item));
            await RefreshAfterOperationAsync(item.Identity, $"{item.Name} 启动命令已完成");
        }
        catch (Exception exception)
        {
            StatusMessage = $"启动失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StopItemAsync(TunnelConnection item, bool manual)
    {
        EnsureClientAvailable();
        if (manual) _manualStopped.Add(item.Identity);
        CancelReconnect(item.Identity, resetAttempts: true);
        IsBusy = true;
        StatusMessage = $"正在停止 {item.Name}...";
        try
        {
            await _operations.StopAsync(item);
            if (!string.IsNullOrWhiteSpace(item.LogPath)) _lastLogPaths[item.Identity] = item.LogPath;
            await RefreshAfterOperationAsync(item.Identity, $"{item.Name} 已停止");
        }
        catch (Exception exception)
        {
            StatusMessage = $"停止失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshAfterOperationAsync(string? identity, string completedMessage)
    {
        var items = await _inventory.GetConnectionsAsync();
        var refreshed = await ReadStatusesAsync(items, CancellationToken.None);
        Connections.Clear();
        foreach (var item in refreshed)
        {
            if (!string.IsNullOrWhiteSpace(item.LogPath)) _lastLogPaths[item.Identity] = item.LogPath;
            Connections.Add(item);
        }
        SelectedConnection = identity is null
            ? Connections.FirstOrDefault()
            : Connections.FirstOrDefault(item => string.Equals(item.Identity, identity, StringComparison.OrdinalIgnoreCase)) ?? Connections.FirstOrDefault();
        StatusMessage = completedMessage;
        NotifyOverviewState();
        if (SelectedConnection is not null && _lastLogPaths.TryGetValue(SelectedConnection.Identity, out var path)) CurrentLogPath = path;
    }

    private async Task ApplyAutoConnectAsync()
    {
        var candidates = Connections
            .Where(item => FindPreference(item) is { Enabled: true, AutoConnect: true })
            .Where(item => !item.ProcessRunning && !_manualStopped.Contains(item.Identity))
            .ToArray();
        if (candidates.Length == 0) return;

        IsBusy = true;
        try
        {
            foreach (var item in candidates)
            {
                try
                {
                    await _operations.StartAsync(item, ReadSavedSecret(item));
                }
                catch (Exception exception)
                {
                    StatusMessage = $"{item.Name} 自动连接失败：{exception.Message}";
                }
            }

            var statuses = await ReadStatusesAsync(Connections.ToArray(), CancellationToken.None);
            foreach (var status in statuses)
            {
                ReplaceConnection(status);
                if (!string.IsNullOrWhiteSpace(status.LogPath)) _lastLogPaths[status.Identity] = status.LogPath;
            }
            NotifyOverviewState();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private ProfilePreference? FindPreference(TunnelConnection item)
    {
        foreach (var key in PreferenceKeys(item)) if (Settings.ProfilePreferences.TryGetValue(key, out var pref)) return pref;
        return null;
    }

    private static IEnumerable<string> PreferenceKeys(TunnelConnection item)
    {
        yield return item.Identity;
        if (item.HasProfile) { yield return $"profile:{item.ProfileName}"; yield return item.ProfileName; }
        if (item.HasRuntime) yield return item.RuntimeAlias;
        if (!string.IsNullOrWhiteSpace(item.RuntimeProfileName)) yield return $"profile:{item.RuntimeProfileName}";
    }

    private static IEnumerable<string> CredentialIds(TunnelConnection item)
    {
        var values = new List<string>();
        if (item.ProfileListed && !string.IsNullOrWhiteSpace(item.ProfileName)) values.Add(item.ProfileName);
        foreach (var value in new[] { item.CredentialId, item.RuntimeAlias, item.ProfileName, item.RuntimeProfileName, item.Name })
            if (!string.IsNullOrWhiteSpace(value) && !values.Contains(value, StringComparer.OrdinalIgnoreCase)) values.Add(value);
        return values;
    }

    private static string CredentialWriteId(TunnelConnection item) => CredentialIds(item).First();

    private void ReplaceConnection(TunnelConnection updated)
    {
        var existing = Connections.FirstOrDefault(item => string.Equals(item.Identity, updated.Identity, StringComparison.OrdinalIgnoreCase));
        if (existing is null) return;
        var index = Connections.IndexOf(existing);
        Connections[index] = updated;
        if (SelectedConnection is not null && string.Equals(SelectedConnection.Identity, updated.Identity, StringComparison.OrdinalIgnoreCase)) SelectedConnection = updated;
    }

    private void EnsureClientAvailable()
    {
        _ = _operations.ResolveExecutablePath();
        IsClientAvailable = true;
    }

    private void NotifyActionState()
    {
        OnPropertyChanged(nameof(CanStartSelected));
        OnPropertyChanged(nameof(CanStopSelected));
        OnPropertyChanged(nameof(CanRestartSelected));
        OnPropertyChanged(nameof(CanEditSelected));
        OnPropertyChanged(nameof(CanDeleteSelected));
        OnPropertyChanged(nameof(CanDiagnoseSelected));
        OnPropertyChanged(nameof(CanOpenConfigSelected));
        OnPropertyChanged(nameof(CanOpenLogSelected));
        RefreshCommand.NotifyCanExecuteChanged();
        StartSelectedCommand.NotifyCanExecuteChanged();
        StopSelectedCommand.NotifyCanExecuteChanged();
        RestartSelectedCommand.NotifyCanExecuteChanged();
        DiagnoseSelectedCommand.NotifyCanExecuteChanged();
    }

    private void NotifyOverviewState()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(HealthyCount));
        OnPropertyChanged(nameof(ProblemCount));
        OnPropertyChanged(nameof(ReadinessState));
        OnPropertyChanged(nameof(HasConnections));
        OnPropertyChanged(nameof(HasSelectedConnection));
        NotifyActionState();
    }

    private static bool PathsEqual(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
