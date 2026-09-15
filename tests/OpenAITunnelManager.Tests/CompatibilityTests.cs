using OpenAITunnelManager.Infrastructure.Settings;
using OpenAITunnelManager.Infrastructure.Windows;
using Xunit;

namespace OpenAITunnelManager.Tests;

public sealed class CompatibilityTests
{
    [Fact]
    public async Task BrokenSettingsArePreservedAsBrokenBackup()
    {
        var token = TestContext.Current.CancellationToken;
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "config", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{broken", token);

        var loaded = await new JsonSettingsStore(path).LoadAsync(token);

        Assert.Empty(loaded.ProfilePreferences);
        Assert.True(File.Exists(path + ".broken"));
    }

    [Fact]
    public async Task PythonLegacySettingsAreMigratedWithoutTunnelDefinitions()
    {
        var token = TestContext.Current.CancellationToken;
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "config", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const string legacy = """
            {
              "schema_version": 1,
              "binary_path": "C:/Tools/tunnel-client.exe",
              "close_to_tray": false,
              "start_with_windows": true,
              "refresh_interval_ms": 5000,
              "profile_preferences": {
                "profile:kept": {"auto_connect": true, "auto_reconnect": false, "enabled": true}
              },
              "tunnels": [
                {
                  "alias": "idea",
                  "tunnel_id": "old-private-definition",
                  "mcp_target": "http://old-private-target",
                  "api_key": "must-not-survive",
                  "auto_connect": true,
                  "auto_reconnect": true,
                  "enabled": false
                }
              ]
            }
            """;
        await File.WriteAllTextAsync(path, legacy, token);

        var loaded = await new JsonSettingsStore(path).LoadAsync(token);

        Assert.Equal(2, loaded.SchemaVersion);
        Assert.Equal("C:/Tools/tunnel-client.exe", loaded.TunnelClientPath);
        Assert.False(loaded.CloseToTray);
        Assert.True(loaded.StartWithWindows);
        Assert.Equal(5000, loaded.RefreshIntervalMs);
        Assert.True(loaded.ProfilePreferences["profile:kept"].AutoConnect);
        Assert.True(loaded.ProfilePreferences["idea"].AutoConnect);
        Assert.True(loaded.ProfilePreferences["idea"].AutoReconnect);
        Assert.False(loaded.ProfilePreferences["idea"].Enabled);

        var rewritten = await File.ReadAllTextAsync(path, token);
        Assert.Contains("\"schemaVersion\": 2", rewritten, StringComparison.Ordinal);
        Assert.Contains("\"tunnelClientPath\"", rewritten, StringComparison.Ordinal);
        Assert.DoesNotContain("tunnels", rewritten, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("old-private-definition", rewritten, StringComparison.Ordinal);
        Assert.DoesNotContain("old-private-target", rewritten, StringComparison.Ordinal);
        Assert.DoesNotContain("must-not-survive", rewritten, StringComparison.Ordinal);
        Assert.DoesNotContain("mcp_target", rewritten, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppDataPathsDefaultToLocalAppDataRoot()
    {
        using var temp = new TempDirectory();
        var baseDirectory = Path.Combine(temp.Path, "app");
        var localAppData = Path.Combine(temp.Path, "local");
        Directory.CreateDirectory(baseDirectory);
        Directory.CreateDirectory(localAppData);

        var paths = new AppDataPaths(baseDirectory, localAppData);

        Assert.False(paths.IsPortable);
        Assert.False(paths.PortableRequested);
        Assert.Equal(Path.GetFullPath(Path.Combine(localAppData, "OpenAITunnelManager")), paths.RootDirectory);
        Assert.Equal(Path.Combine(paths.ConfigDirectory, "settings.json"), paths.SettingsPath);
        Assert.True(Directory.Exists(paths.LogsDirectory));
        Assert.True(Directory.Exists(paths.StateDirectory));
    }

    [Fact]
    public void PortableFlagUsesWritableExecutableDirectory()
    {
        using var temp = new TempDirectory();
        var baseDirectory = Path.Combine(temp.Path, "portable-app");
        var localAppData = Path.Combine(temp.Path, "local");
        Directory.CreateDirectory(baseDirectory);
        Directory.CreateDirectory(localAppData);
        File.WriteAllText(Path.Combine(baseDirectory, "portable.flag"), string.Empty);

        var paths = new AppDataPaths(baseDirectory, localAppData);

        Assert.True(paths.PortableRequested);
        Assert.True(paths.IsPortable);
        Assert.Equal(Path.GetFullPath(baseDirectory), paths.RootDirectory);
        Assert.Equal(Path.Combine(baseDirectory, "config", "settings.json"), paths.SettingsPath);
    }

    [Fact]
    public void WindowsCredentialManagerRoundTrip()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = new WindowsCredentialStore();
        var key = "test-" + Guid.NewGuid().ToString("N");
        var secret = "sk-test-" + Guid.NewGuid().ToString("N");
        try
        {
            store.Set(key, secret);
            Assert.Equal(secret, store.Get(key));
        }
        finally
        {
            store.Delete(key);
        }
        Assert.Null(store.Get(key));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OpenAITunnelManager.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
