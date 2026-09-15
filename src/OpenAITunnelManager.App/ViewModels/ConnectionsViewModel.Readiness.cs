namespace OpenAITunnelManager.App.ViewModels;

public sealed partial class ConnectionsViewModel
{
    public ApplicationReadinessState ReadinessState =>
        IsClientAvailable
            ? (Connections.Count == 0 ? ApplicationReadinessState.NoConnections : ApplicationReadinessState.Ready)
            : string.IsNullOrWhiteSpace(TunnelClientPath)
                ? ApplicationReadinessState.TunnelClientNotConfigured
                : ApplicationReadinessState.TunnelClientInvalid;

    public bool HasConnections => Connections.Count > 0;
    public bool HasSelectedConnection => SelectedConnection is not null;
    public bool HasCurrentLog => SelectedConnection is not null &&
                                 (!string.IsNullOrWhiteSpace(CurrentLogPath) || !string.IsNullOrWhiteSpace(RawLog));
}
