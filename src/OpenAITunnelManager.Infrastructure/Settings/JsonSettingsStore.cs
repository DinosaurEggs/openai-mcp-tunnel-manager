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
        SettingsPath = settingsPath ?? AppDataPaths.Current.SettingsPath;
    }

    public string SettingsPath { get; }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath)) return new AppSettings();

        try
        {
            var text = await File.ReadAllTextAsync(SettingsPath, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("settings.json 根节点必须是对象");

            var (settings, migrated) = ParseSettings(document.RootElement);
            settings = Normalize(settings);
            if (migrated) await SaveAsync(settings, cancellationToken).ConfigureAwait(false);
            return settings;
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
        settings.SchemaVersion = 2;

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

    private static (AppSettings Settings, bool Migrated) ParseSettings(JsonElement root)
    {
        var schema = ReadInt(root, 1, "schemaVersion", "schema_version");
        var migrated = schema < 2 || HasProperty(root,
            "schema_version",
            "binary_path",
            "close_to_tray",
            "start_with_windows",
            "refresh_interval_ms",
            "refreshIntervalMs",
            "profile_preferences",
            "tunnels");
        var settings = new AppSettings
        {
            SchemaVersion = 2,
            TunnelClientPath = ReadString(root, "tunnelClientPath", "binaryPath", "binary_path"),
            CloseToTray = ReadBool(root, true, "closeToTray", "close_to_tray"),
            StartWithWindows = ReadBool(root, false, "startWithWindows", "start_with_windows"),
            ProfileDirectoryOverride = ReadString(root, "profileDirectoryOverride", "profile_directory_override"),
            StateDirectoryOverride = ReadString(root, "stateDirectoryOverride", "state_directory_override"),
            ProfilePreferences = new Dictionary<string, ProfilePreference>(StringComparer.OrdinalIgnoreCase)
        };

        if (TryProperty(root, out var preferences, "profilePreferences", "profile_preferences") && preferences.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in preferences.EnumerateObject())
            {
                var key = property.Name.Trim();
                if (key.Length == 0 || property.Value.ValueKind != JsonValueKind.Object) continue;
                settings.ProfilePreferences[key] = ParsePreference(property.Value);
            }
        }

        if (TryProperty(root, out var tunnels, "tunnels") && tunnels.ValueKind == JsonValueKind.Array)
        {
            foreach (var tunnel in tunnels.EnumerateArray())
            {
                if (tunnel.ValueKind != JsonValueKind.Object) continue;
                var name = ReadString(tunnel, "alias", "name").Trim();
                if (name.Length == 0 || settings.ProfilePreferences.ContainsKey(name)) continue;
                settings.ProfilePreferences[name] = ParsePreference(tunnel);
            }
        }

        return (settings, migrated);
    }

    private static ProfilePreference ParsePreference(JsonElement element) => new()
    {
        AutoConnect = ReadBool(element, false, "autoConnect", "auto_connect"),
        AutoReconnect = ReadBool(element, false, "autoReconnect", "auto_reconnect"),
        Enabled = ReadBool(element, true, "enabled")
    };

    private static AppSettings Normalize(AppSettings settings)
    {
        settings.SchemaVersion = 2;
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

    private static bool HasProperty(JsonElement element, params string[] names) =>
        names.Any(name => TryProperty(element, out _, name));

    private static bool TryProperty(JsonElement element, out JsonElement value, params string[] names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (names.Any(name => string.Equals(name, property.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string ReadString(JsonElement element, params string[] names)
    {
        if (!TryProperty(element, out var value, names)) return string.Empty;
        return value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? string.Empty : string.Empty;
    }

    private static bool ReadBool(JsonElement element, bool fallback, params string[] names)
    {
        if (!TryProperty(element, out var value, names)) return fallback;
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) return value.GetBoolean();
        return value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed) ? parsed : fallback;
    }

    private static int ReadInt(JsonElement element, int fallback, params string[] names)
    {
        if (!TryProperty(element, out var value, names)) return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number) ? number : fallback;
    }

    private void TryBackupBrokenSettings()
    {
        try
        {
            var backup = SettingsPath + ".broken";
            if (!File.Exists(backup)) File.Copy(SettingsPath, backup);
        }
        catch
        {
        }
    }
}
