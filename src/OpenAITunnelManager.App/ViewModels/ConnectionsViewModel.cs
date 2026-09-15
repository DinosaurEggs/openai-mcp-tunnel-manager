using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenAITunnelManager.Core.Abstractions;
using OpenAITunnelManager.Core.Models;
using OpenAITunnelManager.Infrastructure.TunnelClient;

namespace OpenAITunnelManager.App.ViewModels;

public partial class ConnectionsViewModel : ObservableObject
{
    private readonly ITunnelClientService _inventory;
    private readonly ITunnelClientOperations _operations;
    private readonly ISettingsStore _settingsStore;
    private readonly ICredentialStore _credentials;
    private readonly IAutostartService _autostart;
    private readonly TunnelClientOptions _options;
    private readonly HashSet<string> _manualStopped = new(StringComparer.OrdinalIgnoreCase);
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
    [ObservableProperty] public partial int RefreshIntervalSeconds { get; set; } = 4;
    [ObservableProperty] public partial bool CloseToTray { get; set; } = true;
    [ObservableProperty] public partial bool StartWithWindows { get; set; }
    [ObservableProperty] public partial string ProfileDirectoryOverride { get; set; } = string.Empty;
    [ObservableProperty] public partial string StateDirectoryOverride { get; set; } = string.Empty;
    [ObservableProperty] public partial string RawLog { get; set; } = string.Empty;
    [ObservableProperty] public partial string VisibleLog { get; set; } = string.Empty;
    [ObservableProperty] public partial string LogSearch { get; set; } = string.Empty;
    [ObservableProperty] public partial string LogLevel { get; set; } = "全部";
    [ObservableProperty] public partial bool LogAutoRefresh { get; set; } = true;
    [ObservableProperty] public partial string CurrentLogPath { get; set; } = string.Empty;
    [ObservableProperty] public partial string DiagnosticText { get; set; } = string.Empty;
    [ObservableProperty] public partial string HealthText { get; set; } = string.Empty;

    public bool CanStartSelected => IsClientAvailable && !IsBusy && SelectedConnection is { ProcessRunning: false };
    public bool CanStopSelected => IsClientAvailable && !IsBusy && SelectedConnection is { ProcessRunning: true };
    public bool CanRestartSelected => CanStopSelected;
    public bool CanEditSelected => IsClientAvailable && !IsBusy && SelectedConnection is { ProfileListed: true, HasProfile: true };
    public bool CanDeleteSelected => IsClientAvailable && !IsBusy && SelectedConnection is not null;
    public bool CanDiagnoseSelected => IsClientAvailable && !IsBusy && SelectedConnection is not null;
    public string SettingsPath => _settingsStore.SettingsPath;

    partial void OnSelectedConnectionChanged(TunnelConnection? value)
    {
        if (value is not null && _lastLogPaths.TryGetValue(value.Identity, out var path)) CurrentLogPath = path;
        else CurrentLogPath = value?.LogPath ?? string.Empty;
        NotifyActionState();
    }

    partial void OnIsBusyChanged(bool value) => NotifyActionState();
    partial void OnIsClientAvailableChanged(bool value) => NotifyActionState();
    partial void OnLogSearchChanged(string value) => RenderLog();
    partial void OnLogLevelChanged(string value) => RenderLog();

    public async Task InitializeAsync()
    {
        Settings = await _settingsStore.LoadAsync();
        TunnelClientPath = Settings.TunnelClientPath;
        RefreshIntervalSeconds = Math.Max(2, Settings.NormalizedRefreshIntervalMs / 1000);
        CloseToTray = Settings.CloseToTray;
        StartWithWindows = _autostart.IsEnabled();
        ProfileDirectoryOverride = Settings.ProfileDirectoryOverride;
        StateDirectoryOverride = Settings.StateDirectoryOverride;
        ApplySettingsToOptions();
        await RefreshAsync();
    }

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
            var items = new List<TunnelConnection>();
            foreach (var item in itemsTask.Result)
            {
                var effective = await _operations.GetStatusAsync(item);
                if (!string.IsNullOrWhiteSpace(effective.LogPath)) _lastLogPaths[effective.Identity] = effective.LogPath;
                items.Add(effective);
            }
            Connections.Clear();
            foreach (var item in items) Connections.Add(item);
            SelectedConnection = Connections.FirstOrDefault(item => item.Identity == selectedIdentity) ?? Connections.FirstOrDefault();
            StatusMessage = Connections.Count == 0 ? "未发现 Profile 或 Runtime" : $"已从 tunnel-client 读取 {Connections.Count} 个配置/运行实例";
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
        await EvaluateAutoReconnectAsync();
    }

    private bool CanRefresh() => !IsBusy;

    public async Task SaveSettingsAsync()
    {
        var path = TunnelClientPath.Trim();
        if (!string.IsNullOrWhiteSpace(path) && !File.Exists(path)) throw new FileNotFoundException("选择的 tunnel-client.exe 不存在", path);
        Settings.TunnelClientPath = path;
        Settings.RefreshIntervalMs = Math.Max(2, RefreshIntervalSeconds) * 1000;
        Settings.CloseToTray = CloseToTray;
        Settings.StartWithWindows = StartWithWindows;
        Settings.ProfileDirectoryOverride = ProfileDirectoryOverride.Trim();
        Settings.StateDirectoryOverride = StateDirectoryOverride.Trim();
        _autostart.SetEnabled(StartWithWindows);
        await _settingsStore.SaveAsync(Settings);
        ApplySettingsToOptions();
        StatusMessage = $"设置已保存：{SettingsPath}";
        await RefreshAsync();
    }

    public void SetTunnelClientPath(string path) => TunnelClientPath = path;

    private void ApplySettingsToOptions() => _options.Apply(Settings);

    public ProfilePreference GetSelectedPreferenceCopy()
    {
        var item = SelectedConnection ?? throw new InvalidOperationException("请先选择一个配置");
        var pref = FindPreference(item) ?? new ProfilePreference();
        return new ProfilePreference { Enabled = pref.Enabled, AutoConnect = pref.AutoConnect, AutoReconnect = pref.AutoReconnect };
    }

    public async Task SaveSelectedPreferenceAsync(ProfilePreference preference, string? newSecret = null, bool deleteSecret = false)
    {
        var item = SelectedConnection ?? throw new InvalidOperationException("请先选择一个配置");
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
        StatusMessage = $"已保存 {item.Name} 的本机偏好";
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

    private void NotifyActionState()
    {
        OnPropertyChanged(nameof(CanStartSelected)); OnPropertyChanged(nameof(CanStopSelected)); OnPropertyChanged(nameof(CanRestartSelected));
        OnPropertyChanged(nameof(CanEditSelected)); OnPropertyChanged(nameof(CanDeleteSelected)); OnPropertyChanged(nameof(CanDiagnoseSelected));
        RefreshCommand.NotifyCanExecuteChanged(); StartSelectedCommand.NotifyCanExecuteChanged(); StopSelectedCommand.NotifyCanExecuteChanged(); RestartSelectedCommand.NotifyCanExecuteChanged();
    }

    private void RenderLog()
    {
        var query = LogSearch.Trim();
        var level = LogLevel;
        VisibleLog = string.Join(Environment.NewLine, RawLog.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => string.IsNullOrWhiteSpace(query) || line.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Where(line => level == "全部" || line.Contains(level, StringComparison.OrdinalIgnoreCase)));
    }
}
