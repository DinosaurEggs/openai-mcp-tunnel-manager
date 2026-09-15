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
{
    public bool HasRuntime => !string.IsNullOrWhiteSpace(RuntimeAlias);

    public string StatusLabel => State switch
    {
        RuntimeState.Ready => "运行中",
        RuntimeState.Running => "运行中",
        RuntimeState.Starting => "正在启动",
        RuntimeState.Stopped => "已停止",
        RuntimeState.Configured => "未启动",
        RuntimeState.Stale => "配置失效",
        RuntimeState.Error => "异常",
        _ => "未知"
    };

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
