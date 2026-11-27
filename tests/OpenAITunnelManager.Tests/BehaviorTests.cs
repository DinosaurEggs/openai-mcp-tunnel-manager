using System.Diagnostics;
using OpenAITunnelManager.Core.Abstractions;
using OpenAITunnelManager.Core.Models;
using OpenAITunnelManager.Infrastructure.Settings;
using OpenAITunnelManager.Infrastructure.TunnelClient;
using Xunit;

namespace OpenAITunnelManager.Tests;

public sealed class BehaviorTests
{
    private const string TunnelId = "tunnel_0123456789abcdef0123456789abcdef";

    [Fact]
    public void ProfileSpec_ValidatesPythonCompatibleRules()
    {
        var valid = new ProfileSpec("idea", TunnelId, McpType.Http, "http://127.0.0.1:64343/stream");
        Assert.Empty(valid.Validate());
        Assert.Empty((valid with { TunnelId = "tunnel_ab12_0123456789abcdef0123456789abcdef" }).Validate());
        Assert.NotEmpty((valid with { TunnelId = "tunnel_abc_0123456789abcdef0123456789abcdef" }).Validate());

        var badName = new ProfileSpec(" bad profile", TunnelId, McpType.Http, "http://127.0.0.1:1/mcp");
        Assert.Contains(badName.Validate(), static message => message.Contains("Profile 名称", StringComparison.Ordinal));

        var badTunnel = new ProfileSpec("idea", "tunnel_BAD", McpType.Http, "http://127.0.0.1:1/mcp");
        Assert.Contains(badTunnel.Validate(), static message => message.Contains("Tunnel ID", StringComparison.Ordinal));

        var badUrl = new ProfileSpec("idea", TunnelId, McpType.Http, "not-a-url");
        Assert.Contains(badUrl.Validate(), static message => message.Contains("HTTP MCP", StringComparison.Ordinal));
    }

    [Fact]
    public void ProfileDocumentEditor_PreservesAdvancedYamlAndComments()
    {
        var source = """
            # keep-this-comment
            config_version: 1
            control_plane:
              tunnel_id: "tunnel_0123456789abcdef0123456789abcdef"
              api_key: "env:CONTROL_PLANE_API_KEY"
            mcp:
              server_urls:
                - channel: main
                  url: "http://127.0.0.1:64343/stream"
                - channel: secondary
                  url: "http://127.0.0.1:9000/mcp"
            advanced_custom:
              keep: true
            """;

        var metadata = ProfileDocumentEditor.ReadMetadata(source);
        var updated = ProfileDocumentEditor.ApplyCommonFields(
            source,
            metadata.TunnelId,
            metadata.TargetKind,
            metadata.TargetValue,
            "tunnel_11111111111111111111111111111111",
            "http://127.0.0.1:7444/mcp");

        Assert.Contains("# keep-this-comment", updated, StringComparison.Ordinal);
        Assert.Contains("advanced_custom:", updated, StringComparison.Ordinal);
        Assert.Contains("keep: true", updated, StringComparison.Ordinal);
        Assert.Contains("secondary", updated, StringComparison.Ordinal);
        Assert.Contains("9000/mcp", updated, StringComparison.Ordinal);
        Assert.Contains("tunnel_11111111111111111111111111111111", updated, StringComparison.Ordinal);
        Assert.Contains("7444/mcp", updated, StringComparison.Ordinal);
    }

    [Fact]
    public void ProfileDocumentEditor_UsesChannelRegardlessOfFieldOrder()
    {
        var source = """
            control_plane:
              tunnel_id: tunnel_0123456789abcdef0123456789abcdef
            mcp:
              server_urls:
                - url: http://127.0.0.1:8001/mcp
                  channel: secondary
                - url: http://127.0.0.1:8002/mcp # preserve this
                  channel: main
            """;
        var updated = ProfileDocumentEditor.ApplyCommonFields(source, TunnelId,
            "server_url", "http://127.0.0.1:8002/mcp", TunnelId, "http://127.0.0.1:8003/mcp");
        Assert.Contains("8001/mcp", updated);
        Assert.Contains("8003/mcp", updated);
        Assert.DoesNotContain("8002/mcp", updated);
        Assert.Contains("# preserve this", updated);
    }

    [Fact]
    public async Task SettingsStore_OnlyPersistsManagerPreferences()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "config", "settings.json");
        var store = new JsonSettingsStore(path);
        var settings = new AppSettings
        {
            TunnelClientPath = @"C:\Tools\tunnel-client.exe",
            CloseToTray = true,
            StartWithWindows = true,
            ProfilePreferences = new Dictionary<string, ProfilePreference>(StringComparer.OrdinalIgnoreCase)
            {
                ["profile:idea"] = new() { Enabled = true, AutoConnect = true, AutoReconnect = true }
            }
        };

        await store.SaveAsync(settings, cancellationToken);
        var text = await File.ReadAllTextAsync(path, cancellationToken);
        Assert.Contains("tunnelClientPath", text, StringComparison.Ordinal);
        Assert.Contains("profilePreferences", text, StringComparison.Ordinal);
        Assert.DoesNotContain("refreshInterval", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(TunnelId, text, StringComparison.Ordinal);
        Assert.DoesNotContain("mcpTarget", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKey", text, StringComparison.OrdinalIgnoreCase);

        var loaded = await store.LoadAsync(cancellationToken);
        Assert.True(loaded.ProfilePreferences["PROFILE:IDEA"].AutoConnect);
    }

    [Fact]
    public async Task SettingsStore_ConcurrentSavesRetainLatestSnapshot()
    {
        var token = TestContext.Current.CancellationToken;
        using var temp = new TempDirectory();
        var store = new JsonSettingsStore(Path.Combine(temp.Path, "settings.json"));
        var settings = new AppSettings { ThemeMode = "dark" };
        var first = store.SaveAsync(settings, token);
        settings.ThemeMode = "light";
        var second = store.SaveAsync(settings, token);
        await Task.WhenAll(first, second);
        Assert.Equal("light", (await store.LoadAsync(token)).ThemeMode);
    }

    [Fact]
    public void RuntimeAndProfileWithSameVisibleName_HaveDistinctIdentities()
    {
        var runtime = Connection("same", profileName: "other", profilePath: @"C:\p\other.yaml", runtimeAlias: "same", profileListed: false);
        var profile = Connection("same", profileName: "same", profilePath: @"C:\p\same.yaml", runtimeAlias: string.Empty, profileListed: true);

        Assert.Equal("runtime:same", runtime.Identity);
        Assert.Equal("profile:same", profile.Identity);
        Assert.NotEqual(runtime.Identity, profile.Identity);
    }

    [Fact]
    public void EmptyDirectoryOverrides_DoNotReplaceInheritedTunnelClientEnvironment()
    {
        var options = new TunnelClientOptions();
        options.Apply(new AppSettings { ProfileDirectoryOverride = "", StateDirectoryOverride = "" });
        var startInfo = new ProcessStartInfo();
        startInfo.Environment["TUNNEL_CLIENT_PROFILE_DIR"] = @"C:\existing\profiles";
        startInfo.Environment["TUNNEL_CLIENT_STATE_DIR"] = @"C:\existing\state";

        options.ApplyChildEnvironment(startInfo);

        Assert.Equal(@"C:\existing\profiles", startInfo.Environment["TUNNEL_CLIENT_PROFILE_DIR"]);
        Assert.Equal(@"C:\existing\state", startInfo.Environment["TUNNEL_CLIENT_STATE_DIR"]);
    }

    [Fact]
    public void TunnelClientProcessRunner_DecodesRedirectedStreamsAsUtf8()
    {
        using var temp = new TempDirectory();
        var executable = Path.Combine(temp.Path, "tunnel-client.exe");
        File.WriteAllBytes(executable, []);
        var options = new TunnelClientOptions { ExecutablePath = executable };
        var runner = new TunnelClientProcessRunner(options);

        var startInfo = runner.CreateStartInfo(["--version"]);

        Assert.Equal("utf-8", startInfo.StandardOutputEncoding?.WebName);
        Assert.Equal("utf-8", startInfo.StandardErrorEncoding?.WebName);
    }

    [Fact]
    public void ExplicitMissingBinary_NeverFallsBackToAnotherTunnelClient()
    {
        var options = new TunnelClientOptions { ExecutablePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe") };
        var error = Assert.Throws<FileNotFoundException>(() => options.ResolveExecutablePath());
        Assert.Contains("已配置的 tunnel-client 不存在", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LogTail_IsBoundedAndReadsNewestLines()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDirectory();
        var log = Path.Combine(temp.Path, "large.log");
        await File.WriteAllLinesAsync(log, Enumerable.Range(0, 10000).Select(static index => $"INFO line {index}"), cancellationToken);
        var operations = new TunnelClientOperations(new TunnelClientOptions(), new FakeInventory());

        var text = await operations.ReadLogTailAsync(log, maxBytes: 8192, maxLines: 80, cancellationToken);

        Assert.Contains("INFO line 9999", text, StringComparison.Ordinal);
        Assert.True(text.Split(Environment.NewLine).Length <= 80);
        Assert.DoesNotContain("INFO line 0" + Environment.NewLine, text, StringComparison.Ordinal);
    }

    private static TunnelConnection Connection(string name, string profileName, string profilePath, string runtimeAlias, bool profileListed) =>
        new(
            Name: name,
            ProfileName: profileName,
            ProfilePath: profilePath,
            ProfileListed: profileListed,
            RuntimeAlias: runtimeAlias,
            RuntimeProfileName: profileName,
            RuntimeProfilePath: profilePath,
            TunnelId: TunnelId,
            TargetKind: "server_url",
            TargetValue: "http://127.0.0.1:64343/stream",
            State: runtimeAlias.Length == 0 ? RuntimeState.Configured : RuntimeState.Stopped,
            ProcessRunning: false,
            Healthy: false,
            Ready: false,
            HealthUrl: string.Empty,
            HealthDetailsUrl: string.Empty,
            McpHealthUrl: string.Empty,
            LogPath: string.Empty,
            ProcessId: null,
            Error: string.Empty);

    [Fact]
    public void ScopedCredentialsAndPreferencesFollowOfficialPaths()
    {
        using var temp = new TempDirectory();
        var profile = Connection("shared", "shared", Path.Combine(temp.Path, "profiles", "shared.yaml"), "", true);
        var runtime = Connection("r1", "shared", profile.ProfilePath, "r1", true) with
        {
            StateRoot = Path.Combine(temp.Path, "state")
        };
        var other = runtime with { StateRoot = Path.Combine(temp.Path, "other-state") };
        Assert.NotEqual(ConnectionScope.Primary(runtime), ConnectionScope.Primary(other));
        Assert.Equal(ConnectionScope.Profile(profile.ProfilePath), ConnectionScope.Keys(runtime).Last());
        var settings = new AppSettings();
        settings.ProfilePreferences[ConnectionScope.Primary(profile)] = new() { AutoConnect = true };
        Assert.True(settings.GetPreference(runtime).AutoConnect);
        settings.ProfilePreferences[ConnectionScope.Primary(runtime)] = new() { Enabled = false };
        Assert.False(settings.GetPreference(runtime).Enabled);
        Assert.True(settings.GetPreference(other).Enabled);
        Assert.NotEqual(ConnectionScope.Primary(profile), ConnectionScope.Primary(profile with
        {
            ProfilePath = Path.Combine(temp.Path, "other-profiles", "shared.yaml")
        }));
    }

    [Fact]
    public void LegacyCredentialMigrationDoesNotGuessAmbiguousNames()
    {
        using var temp = new TempDirectory();
        var profile = Connection("same", "same", Path.Combine(temp.Path, "same.yaml"), "", true);
        var runtime = Connection("same", "other", Path.Combine(temp.Path, "other.yaml"), "same", true) with
        {
            StateRoot = Path.Combine(temp.Path, "state")
        };
        var credentials = new MemoryCredentials();
        credentials.Set("same", "ambiguous");
        credentials.Set("other", "unique");
        var settings = new AppSettings();
        settings.ProfilePreferences["profile:same"] = new() { AutoConnect = true };
        Assert.Equal(1, ConnectionScope.MigrateLegacy([profile, runtime], settings, credentials));
        Assert.Null(credentials.Get(ConnectionScope.Primary(profile)));
        Assert.Null(credentials.Get(ConnectionScope.Primary(runtime)));
        Assert.Equal("unique", credentials.Get(ConnectionScope.Profile(runtime.ProfilePath)));
        Assert.True(settings.GetPreference(profile).AutoConnect);
        Assert.Equal("ambiguous", credentials.Get("same"));
    }

    [Fact]
    public void ProfileScalarEditingPreservesUnrelatedYaml()
    {
        const string source = """
            # retain comment
            config_version: 1
            control_plane:
              tunnel_id: tunnel_0123456789abcdef0123456789abcdef
            mcp:
              server_urls:
                - channel: main
                  url: http://localhost:8000/mcp
            """;
        var updated = ProfileDocumentEditor.SetSectionScalar(source, "control_plane", "residency", "global");
        updated = ProfileDocumentEditor.SetSectionScalar(updated, "tracing", "enabled", "true", boolean: true);
        Assert.Equal("global", ProfileDocumentEditor.ReadSectionScalar(updated, "control_plane", "residency"));
        Assert.Equal("true", ProfileDocumentEditor.ReadSectionScalar(updated, "tracing", "enabled"));
        Assert.Contains("# retain comment", updated);
        Assert.Contains("http://localhost:8000/mcp", updated);
    }

    private sealed class MemoryCredentials : ICredentialStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
        public string? Get(string credentialId) => _values.GetValueOrDefault(credentialId);
        public void Set(string credentialId, string secret) => _values[credentialId] = secret;
        public void Delete(string credentialId) => _values.Remove(credentialId);
    }

    private sealed class FakeInventory : ITunnelClientService
    {
        public Task<string> GetVersionAsync(CancellationToken cancellationToken = default) => Task.FromResult("fake");
        public Task<IReadOnlyList<TunnelConnection>> GetConnectionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TunnelConnection>>([]);
        public Task<TunnelConnection> GetStatusAsync(TunnelConnection connection, CancellationToken cancellationToken = default, string? secret = null) => Task.FromResult(connection);
        public Task StopRuntimeAsync(string alias, CancellationToken cancellationToken = default) => Task.CompletedTask;
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