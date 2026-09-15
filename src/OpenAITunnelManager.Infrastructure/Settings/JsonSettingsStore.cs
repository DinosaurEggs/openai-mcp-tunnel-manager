using System.Text.Json;
using OpenAITunnelManager.Core.Abstractions;
using OpenAITunnelManager.Core.Models;

namespace OpenAITunnelManager.Infrastructure.Settings;

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public JsonSettingsStore(string? settingsPath = null)
    {
        SettingsPath = settingsPath ?? Path.Combine(AppContext.BaseDirectory, "config", "settings.json");
    }

    public string SettingsPath { get; }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath))
        {
            return new AppSettings();
        }

        try
        {
            await using var stream = File.OpenRead(SettingsPath);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return Normalize(settings ?? new AppSettings());
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            TryBackupBrokenSettings();
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.RefreshIntervalMs = settings.NormalizedRefreshIntervalMs;

        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);

        var tempPath = SettingsPath + ".tmp";
        await using (var stream = new FileStream(
            tempPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(tempPath, SettingsPath, overwrite: true);
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        settings.SchemaVersion = Math.Max(1, settings.SchemaVersion);
        settings.RefreshIntervalMs = settings.NormalizedRefreshIntervalMs;
        settings.TunnelClientPath = settings.TunnelClientPath?.Trim() ?? string.Empty;
        settings.ProfileDirectoryOverride = settings.ProfileDirectoryOverride?.Trim() ?? string.Empty;
        settings.StateDirectoryOverride = settings.StateDirectoryOverride?.Trim() ?? string.Empty;
        settings.ProfilePreferences ??= new Dictionary<string, ProfilePreference>(StringComparer.OrdinalIgnoreCase);

        if (settings.ProfilePreferences.Comparer != StringComparer.OrdinalIgnoreCase)
        {
            settings.ProfilePreferences = new Dictionary<string, ProfilePreference>(
                settings.ProfilePreferences,
                StringComparer.OrdinalIgnoreCase);
        }

        return settings;
    }

    private void TryBackupBrokenSettings()
    {
        try
        {
            var backup = SettingsPath + ".broken";
            if (!File.Exists(backup))
            {
                File.Copy(SettingsPath, backup);
            }
        }
        catch
        {
        }
    }
}
