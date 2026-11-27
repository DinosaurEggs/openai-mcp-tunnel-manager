namespace OpenAITunnelManager.Core.Models;

public enum RuntimeState
{
    Unknown,
    Configured,
    Starting,
    Running,
    Ready,
    Stopped,
    Stale,
    Error
}

public sealed record TunnelConnection(
    string Name,
    string ProfileName,
    string ProfilePath,
    bool ProfileListed,
    string RuntimeAlias,
    string RuntimeProfileName,
    string RuntimeProfilePath,
    string TunnelId,
    string TargetKind,
    string TargetValue,
    RuntimeState State,
    bool ProcessRunning,
    bool Healthy,
    bool Ready,
    string HealthUrl,
    string HealthDetailsUrl,
    string McpHealthUrl,
    string LogPath,
    int? ProcessId,
    string Error)
{
    public TunnelConnection(
        string Name,
        string ProfileName,
        string ProfilePath,
        string RuntimeAlias,
        string RuntimeProfileName,
        string RuntimeProfilePath,
        string TunnelId,
        string TargetKind,
        string TargetValue,
        RuntimeState State,
        bool ProcessRunning,
        bool Healthy,
        bool Ready,
        string HealthUrl,
        string LogPath,
        int? ProcessId,
        string Error)
        : this(Name, ProfileName, ProfilePath,
            !string.IsNullOrWhiteSpace(ProfileName) && !string.IsNullOrWhiteSpace(ProfilePath),
            RuntimeAlias, RuntimeProfileName, RuntimeProfilePath, TunnelId, TargetKind, TargetValue,
            State, ProcessRunning, Healthy, Ready, HealthUrl, string.Empty, string.Empty, LogPath, ProcessId, Error)
    {
    }

    public string StateRoot { get; init; } = string.Empty;
    public IReadOnlyList<string> RuntimeOwnedPaths { get; init; } = [];
    public bool HasStdioTarget { get; init; }
    public string ControlPlaneState { get; init; } = string.Empty;
    public string ControlPlaneReason { get; init; } = string.Empty;
    public string AdminUiUrl { get; init; } = string.Empty;
    public string RemoteResidency { get; init; } = string.Empty;
    public string RemoteLookupStatus { get; init; } = string.Empty;
    public string RunningTargetKind { get; init; } = string.Empty;
    public string RunningTargetValue { get; init; } = string.Empty;
    public string StartedAt { get; init; } = string.Empty;
    public string Residency { get; init; } = string.Empty;
    public string Authentication { get; init; } = string.Empty;
    public DateTimeOffset? RefreshedAt { get; init; }
    public IReadOnlyList<RuntimeRepairAction> RepairActions { get; init; } = [];
    public string ControlPlaneText => string.IsNullOrWhiteSpace(ControlPlaneState) ? "未观测" : ControlPlaneState;
    public string RefreshedText => RefreshedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "未刷新";
    public bool CanOpenAdminUi => Uri.TryCreate(AdminUiUrl, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && string.IsNullOrEmpty(uri.UserInfo);
    public bool ConfigurationPending => ProcessRunning && !string.IsNullOrEmpty(RunningTargetValue) &&
        (!string.Equals(TargetKind, RunningTargetKind, StringComparison.Ordinal) || !string.Equals(TargetValue, RunningTargetValue, StringComparison.Ordinal));
    public string ConfigurationStatusText => ConfigurationPending ? "磁盘 MCP Target 与运行实例不同，重启后生效" :
        ProcessRunning && string.IsNullOrEmpty(RunningTargetValue) ? "尚未获取运行中的 MCP Target" : "未发现 MCP Target 变更";
    public string RepairText => string.Join(Environment.NewLine + Environment.NewLine,
        RepairActions.Select(action => $"{action.Id}: {action.Reason}{Environment.NewLine}{action.Command}"));

    public bool UseForeground { get; init; }
    public bool RunsInForeground => !HasRuntime || UseForeground;
    public string ExecutionModeText => RunsInForeground ? "Profile 前台" : "Native Runtime";
    public bool HasRuntime => !string.IsNullOrWhiteSpace(RuntimeAlias);
    public bool HasProfile => !string.IsNullOrWhiteSpace(ProfileName) && !string.IsNullOrWhiteSpace(ProfilePath);
    public string Identity => HasRuntime ? $"runtime:{RuntimeAlias}" : HasProfile ? $"profile:{ProfileName}" : $"item:{Name}";
    public string CredentialId => HasRuntime ? RuntimeAlias : HasProfile ? ProfileName : Name;

    public string StatusLabel => State switch
    {
        RuntimeState.Ready or RuntimeState.Running => "已启动",
        RuntimeState.Starting => "启动中",
        RuntimeState.Stopped or RuntimeState.Configured => "已停止",
        RuntimeState.Stale => "状态失效",
        RuntimeState.Error => "异常",
        _ => "未知"
    };

    public string StateText => State switch
    {
        RuntimeState.Configured => "仅配置",
        RuntimeState.Stopped => "已停止",
        RuntimeState.Starting => "正在启动",
        RuntimeState.Running => "运行中",
        RuntimeState.Ready => "已就绪",
        RuntimeState.Error => "错误",
        RuntimeState.Stale => "状态失效",
        _ => "未知"
    };

    public string SourceText => ProfileListed && HasRuntime ? "Profile + 运行实例" : HasRuntime ? "运行实例" : "Profile 配置";
    public string ProcessText => ProcessRunning ? "运行中" : State == RuntimeState.Unknown ? "未知" : "未运行";
    public string HealthText => ProcessRunning ? Healthy ? "正常" : "异常/未知" : "-";
    public string ReadyText => ProcessRunning ? Ready ? "已就绪" : "未就绪" : "-";

    public string Subtitle
    {
        get
        {
            var parts = new[] { ProfileName, RuntimeAlias, TargetKind }.Where(static value => !string.IsNullOrWhiteSpace(value));
            return string.Join(" · ", parts);
        }
    }
}

public sealed record RuntimeRepairAction(string Id, string Reason, string Command);
