namespace OpenAITunnelManager.App.ViewModels;

public enum ApplicationReadinessState
{
    TunnelClientNotConfigured,
    TunnelClientInvalid,
    NoConnections,
    Ready
}
