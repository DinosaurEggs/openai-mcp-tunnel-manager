using OpenAITunnelManager.Core.Models;
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

        Assert.Equal(3, loaded.SchemaVersion);
        Assert.Equal(TunnelClientSource.Custom, loaded.TunnelClientSource);
        Assert.True(loaded.TunnelClientSetupCompleted);
        Assert.Equal("C:/Tools/tunnel-client.exe", loaded.TunnelClientPath);
        Assert.False(loaded.CloseToTray);
        Assert.True(loaded.StartWithWindows);
        Assert.True(loaded.ProfilePreferences["profile:kept"].AutoConnect);
        Assert.True(loaded.ProfilePreferences["idea"].AutoConnect);
        Assert.True(loaded.ProfilePreferences["idea"].AutoReconnect);
        Assert.False(loaded.ProfilePreferences["idea"].Enabled);

        var rewritten = await File.ReadAllTextAsync(path, token);
        Assert.Contains("\"schemaVersion\": 3", rewritten, StringComparison.Ordinal);
        Assert.Contains("\"tunnelClientSource\": \"custom\"", rewritten, StringComparison.Ordinal);
        Assert.Contains("\"tunnelClientSetupCompleted\": true", rewritten, StringComparison.Ordinal);
        Assert.Contains("\"tunnelClientPath\"", rewritten, StringComparison.Ordinal);
        Assert.DoesNotContain("refreshInterval", rewritten, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tunnels", rewritten, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("old-private-definition", rewritten, StringComparison.Ordinal);
        Assert.DoesNotContain("old-private-target", rewritten, StringComparison.Ordinal);
        Assert.DoesNotContain("must-not-survive", rewritten, StringComparison.Ordinal);
        Assert.DoesNotContain("mcp_target", rewritten, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Schema2WithoutCustomPathStartsManagedSetupFlow()
    {
        var token = TestContext.Current.CancellationToken;
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "config", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, """
            {
              "schemaVersion": 2,
              "tunnelClientPath": "",
              "closeToTray": true
            }
            """, token);

        var loaded = await new JsonSettingsStore(path).LoadAsync(token);

        Assert.Equal(3, loaded.SchemaVersion);
        Assert.Equal(TunnelClientSource.Managed, loaded.TunnelClientSource);
        Assert.False(loaded.TunnelClientSetupCompleted);
        Assert.Empty(loaded.ManagedTunnelClientVersion);
    }

    [Fact]
    public void AppDataPathsAlwaysUseExecutableDirectory()
    {
        using var temp = new TempDirectory();
        var baseDirectory = Path.Combine(temp.Path, "app");
        Directory.CreateDirectory(baseDirectory);

        var paths = new AppDataPaths(baseDirectory);

        Assert.Equal(Path.GetFullPath(baseDirectory), paths.RootDirectory);
        Assert.Equal(Path.Combine(baseDirectory, "config", "settings.json"), paths.SettingsPath);
        Assert.Equal(Path.Combine(baseDirectory, "logs", "app.log"), paths.ManagerLogPath);
        Assert.Equal(Path.Combine(baseDirectory, "state", "foreground"), paths.ForegroundStateDirectory);
        Assert.Equal(Path.Combine(baseDirectory, "state", "temp"), paths.TempDirectory);
        Assert.Equal(Path.Combine(baseDirectory, "tunnel-client", "versions"), paths.ManagedTunnelClientVersionsDirectory);
        Assert.Equal(
            Path.Combine(baseDirectory, "tunnel-client", "versions", "v1.2.3", "tunnel-client.exe"),
            paths.GetManagedTunnelClientExecutablePath("v1.2.3"));
        Assert.True(Directory.Exists(paths.ConfigDirectory));
        Assert.True(Directory.Exists(paths.LogsDirectory));
        Assert.True(Directory.Exists(paths.StateDirectory));
        Assert.True(Directory.Exists(paths.TempDirectory));
        Assert.True(Directory.Exists(paths.ForegroundStateDirectory));
        Assert.True(Directory.Exists(paths.ManagedTunnelClientVersionsDirectory));
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
