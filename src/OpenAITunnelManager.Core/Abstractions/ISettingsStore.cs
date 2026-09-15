using OpenAITunnelManager.Core.Models;

namespace OpenAITunnelManager.Core.Abstractions;

public interface ISettingsStore
{
    string SettingsPath { get; }
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

public interface ICredentialStore
{
    string? Get(string credentialId);
    void Set(string credentialId, string secret);
    void Delete(string credentialId);
}

public interface IAutostartService
{
    bool IsEnabled();
    void SetEnabled(bool enabled);
}
