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
    public bool HasRuntime => !string.IsNullOrWhiteSpace(RuntimeAlias);
    public bool HasProfile => !string.IsNullOrWhiteSpace(ProfileName) && !string.IsNullOrWhiteSpace(ProfilePath);

    public string Identity => HasRuntime
        ? $"runtime:{RuntimeAlias}"
        : HasProfile
            ? $"profile:{ProfileName}"
            : $"item:{Name}";

    public string CredentialId => HasRuntime ? RuntimeAlias : HasProfile ? ProfileName : Name;

    public string StatusLabel => State switch
    {
        RuntimeState.Ready => "已启动",
        RuntimeState.Running => "已启动",
        RuntimeState.Starting => "正在启动",
        RuntimeState.Stopped => "已停止",
        RuntimeState.Configured => "已停止",
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

    public string SourceText => ProfileListed && HasRuntime
        ? "Profile + 运行实例"
        : HasRuntime
            ? "运行实例"
            : "Profile 配置";

    public string Subtitle
    {
        get
        {
            var parts = new[] { ProfileName, RuntimeAlias, TargetKind }
                .Where(static value => !string.IsNullOrWhiteSpace(value));
            return string.Join(" · ", parts);
        }
    }
}
