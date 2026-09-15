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
    public async Task SettingsStore_OnlyPersistsManagerPreferences()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "config", "settings.json");
        var store = new JsonSettingsStore(path);
        var settings = new AppSettings
        {
            TunnelClientPath = @"C:\Tools\tunnel-client.exe",
            RefreshIntervalMs = 5000,
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
        Assert.DoesNotContain(TunnelId, text, StringComparison.Ordinal);
        Assert.DoesNotContain("mcpTarget", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKey", text, StringComparison.OrdinalIgnoreCase);

        var loaded = await store.LoadAsync(cancellationToken);
        Assert.Equal(5000, loaded.RefreshIntervalMs);
        Assert.True(loaded.ProfilePreferences["PROFILE:IDEA"].AutoConnect);
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

    private sealed class FakeInventory : ITunnelClientService
    {
        public Task<string> GetVersionAsync(CancellationToken cancellationToken = default) => Task.FromResult("fake");
        public Task<IReadOnlyList<TunnelConnection>> GetConnectionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TunnelConnection>>([]);
        public Task<TunnelConnection> GetStatusAsync(TunnelConnection connection, CancellationToken cancellationToken = default) => Task.FromResult(connection);
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
