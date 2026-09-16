using OpenAITunnelManager.Core.Models;

namespace OpenAITunnelManager.Core.Abstractions;

public interface ITunnelClientOperations
{
    event Action<string>? ForegroundProfileExited;

    string ResolveExecutablePath();
    Task<TunnelClientCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default);
    Task<TunnelConnection> GetStatusAsync(TunnelConnection connection, CancellationToken cancellationToken = default);
    Task CreateProfileAsync(ProfileSpec spec, CancellationToken cancellationToken = default);
    Task CreateProfileTextAsync(string name, string text, CancellationToken cancellationToken = default);
    Task<string> ReadProfileTextAsync(string name, string expectedPath, CancellationToken cancellationToken = default);
    Task SaveProfileTextAsync(string name, string expectedPath, string text, CancellationToken cancellationToken = default);
    Task DeleteProfileAsync(string name, string expectedPath, CancellationToken cancellationToken = default);
    Task StartAsync(TunnelConnection connection, string? secret, CancellationToken cancellationToken = default);
    Task StopAsync(TunnelConnection connection, CancellationToken cancellationToken = default);
    Task RestartAsync(TunnelConnection connection, string? secret, CancellationToken cancellationToken = default);
    Task RemoveRuntimeAsync(string alias, CancellationToken cancellationToken = default);
    Task<string> DoctorAsync(TunnelConnection connection, string? secret, CancellationToken cancellationToken = default);
    Task<string> ReadLogTailAsync(string path, int maxBytes = 512 * 1024, int maxLines = 5000, CancellationToken cancellationToken = default);
    Task<string> GetDetailedHealthAsync(TunnelConnection connection, CancellationToken cancellationToken = default);
    Task ShutdownForegroundProfilesAsync();
}
