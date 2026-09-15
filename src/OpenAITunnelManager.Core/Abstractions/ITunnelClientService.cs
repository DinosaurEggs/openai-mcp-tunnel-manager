using OpenAITunnelManager.Core.Models;

namespace OpenAITunnelManager.Core.Abstractions;

public interface ITunnelClientService
{
    Task<string> GetVersionAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TunnelConnection>> GetConnectionsAsync(CancellationToken cancellationToken = default);
    Task<TunnelConnection> GetStatusAsync(TunnelConnection connection, CancellationToken cancellationToken = default);
    Task StopRuntimeAsync(string alias, CancellationToken cancellationToken = default);
}
