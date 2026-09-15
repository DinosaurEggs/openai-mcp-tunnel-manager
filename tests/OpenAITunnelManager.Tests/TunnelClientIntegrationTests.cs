using OpenAITunnelManager.Core.Models;
using OpenAITunnelManager.FakeTunnelClient;
using OpenAITunnelManager.Infrastructure.TunnelClient;
using Xunit;

namespace OpenAITunnelManager.Tests;

public sealed class TunnelClientIntegrationTests : IDisposable
{
    private const string TunnelId = "tunnel_0123456789abcdef0123456789abcdef";
    private readonly string _root;
    private readonly Dictionary<string, string?> _previousEnvironment = new(StringComparer.Ordinal);

    public TunnelClientIntegrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "OpenAITunnelManager.Integration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        SetEnvironment("FAKE_TUNNEL_STATE", Path.Combine(_root, "state.json"));
        SetEnvironment("FAKE_PROFILE_DIR", Path.Combine(_root, "profiles"));
        SetEnvironment("FAKE_RUNTIME_DIR", Path.Combine(_root, "runtime"));
        SetEnvironment("FAKE_STALE_STATUS", null);
    }

    [Fact]
    public async Task CapabilitiesAndProfileInventoryUseRealProcessBoundary()
    {
        var token = TestContext.Current.CancellationToken;
        var (service, operations) = Services();
        var capabilities = await operations.GetCapabilitiesAsync(token);
        Assert.Contains("v0.0.14-fake", capabilities.Version, StringComparison.Ordinal);
        Assert.True(capabilities.Profiles);
        Assert.True(capabilities.Runtimes);
        Assert.True(capabilities.Doctor);

        await operations.CreateProfileAsync(Spec("idea"), token);
        var items = await service.GetConnectionsAsync(token);
        var item = Assert.Single(items);
        Assert.Equal("profile:idea", item.Identity);
        Assert.True(item.ProfileListed);
        Assert.False(item.HasRuntime);
        Assert.Equal(TunnelId, item.TunnelId);
        Assert.Equal("server_url", item.TargetKind);
    }

    [Fact]
    public async Task LinkedRuntimeMergesWithProfileAndCanStopAndReconnect()
    {
        var token = TestContext.Current.CancellationToken;
        var (service, operations) = Services();
        await operations.CreateProfileAsync(Spec("idea"), token);
        var profile = Assert.Single(await service.GetConnectionsAsync(token));
        var runtimeSeed = RuntimeSeed(profile, "idea");

        await operations.StartAsync(runtimeSeed, "secret-key", token);
        var running = Assert.Single(await service.GetConnectionsAsync(token));
        Assert.Equal("runtime:idea", running.Identity);
        Assert.True(running.ProfileListed);
        Assert.True(running.ProcessRunning);
        Assert.True(running.Ready);
        Assert.NotEmpty(running.LogPath);

        await operations.StopAsync(running, token);
        var stopped = await service.GetStatusAsync(running, token);
        Assert.Equal(RuntimeState.Stopped, stopped.State);
        Assert.False(stopped.ProcessRunning);

        await operations.StartAsync(stopped, "secret-key", token);
        var reconnected = await service.GetStatusAsync(stopped, token);
        Assert.Equal(RuntimeState.Ready, reconnected.State);
        Assert.True(reconnected.ProcessRunning);
    }

    [Fact]
    public async Task MultipleRuntimeAliasesForOneProfileAreNotCollapsed()
    {
        var token = TestContext.Current.CancellationToken;
        var (service, operations) = Services();
        await operations.CreateProfileAsync(Spec("shared"), token);
        var profile = Assert.Single(await service.GetConnectionsAsync(token));

        await operations.StartAsync(RuntimeSeed(profile, "r1"), "secret", token);
        await operations.StartAsync(RuntimeSeed(profile, "r2"), "secret", token);

        var items = await service.GetConnectionsAsync(token);
        Assert.Equal(2, items.Count);
        Assert.Equal(new[] { "r1", "r2" }, items.Select(static item => item.RuntimeAlias).Order(StringComparer.Ordinal).ToArray());
        Assert.All(items, static item => Assert.True(item.ProfileListed));
    }

    [Fact]
    public async Task RuntimeAliasAndUnrelatedProfileWithSameNameStayDistinct()
    {
        var token = TestContext.Current.CancellationToken;
        var (service, operations) = Services();
        await operations.CreateProfileAsync(Spec("same"), token);
        await operations.CreateProfileAsync(Spec("other"), token);
        var other = (await service.GetConnectionsAsync(token)).Single(item => item.ProfileName == "other");

        await operations.StartAsync(RuntimeSeed(other, "same"), "secret", token);
        var items = await service.GetConnectionsAsync(token);
        var identities = items.Select(static item => item.Identity).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("runtime:same", identities);
        Assert.Contains("profile:same", identities);
        Assert.Equal(2, items.Count(item => item.Name == "same"));
    }

    [Fact]
    public async Task RuntimeUsingDifferentProfilePathDoesNotClaimListedProfile()
    {
        var token = TestContext.Current.CancellationToken;
        var (service, operations) = Services();
        await operations.CreateProfileAsync(Spec("same"), token);
        var listed = Assert.Single(await service.GetConnectionsAsync(token));
        var customDirectory = Path.Combine(_root, "custom-runtime");
        Directory.CreateDirectory(customDirectory);
        var customPath = Path.Combine(customDirectory, "same.yaml");
        File.Copy(listed.ProfilePath, customPath);

        var runtime = RuntimeSeed(listed, "same") with { RuntimeProfilePath = customPath, ProfilePath = customPath };
        await operations.StartAsync(runtime, "secret", token);
        var items = await service.GetConnectionsAsync(token);
        var runtimeItem = items.Single(item => item.Identity == "runtime:same");
        Assert.False(runtimeItem.ProfileListed);
        Assert.Equal(Path.GetFullPath(customPath), Path.GetFullPath(runtimeItem.RuntimeProfilePath));
        Assert.Contains(items, static item => item.Identity == "profile:same");
    }

    [Fact]
    public async Task ProfileForegroundLifecycleIsManagedByManager()
    {
        var token = TestContext.Current.CancellationToken;
        var (service, operations) = Services();
        await operations.CreateProfileAsync(Spec("foreground"), token);
        var profile = Assert.Single(await service.GetConnectionsAsync(token));
        try
        {
            await operations.StartAsync(profile, "secret", token);
            var running = await operations.GetStatusAsync(profile, token);
            Assert.True(running.ProcessRunning);
            Assert.Equal(RuntimeState.Starting, running.State);
            Assert.True(File.Exists(running.LogPath));

            await operations.StopAsync(profile, token);
            var stopped = await operations.GetStatusAsync(profile, token);
            Assert.False(stopped.ProcessRunning);
            Assert.Equal(RuntimeState.Configured, stopped.State);
        }
        finally
        {
            await operations.ShutdownForegroundProfilesAsync();
        }
    }

    [Fact]
    public async Task DoctorUsesProfileAndCustomRuntimeProfileFile()
    {
        var token = TestContext.Current.CancellationToken;
        var (service, operations) = Services();
        await operations.CreateProfileAsync(Spec("doctor"), token);
        var profile = Assert.Single(await service.GetConnectionsAsync(token));
        Assert.Contains("Doctor passed", await operations.DoctorAsync(profile, "secret", token), StringComparison.Ordinal);

        var customDirectory = Path.Combine(_root, "doctor-custom");
        Directory.CreateDirectory(customDirectory);
        var customPath = Path.Combine(customDirectory, "custom.yaml");
        File.Copy(profile.ProfilePath, customPath);
        var customRuntime = RuntimeSeed(profile, "custom") with
        {
            ProfileName = "not-listed",
            RuntimeProfileName = "not-listed",
            ProfilePath = customPath,
            RuntimeProfilePath = customPath,
            ProfileListed = false
        };
        await operations.StartAsync(customRuntime, "secret", token);
        var current = (await service.GetConnectionsAsync(token)).Single(item => item.RuntimeAlias == "custom");
        Assert.False(current.ProfileListed);
        Assert.Contains("Doctor passed", await operations.DoctorAsync(current, "secret", token), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProfileSaveUsesOfficialValidatorAndKeepsOriginalOnFailure()
    {
        var token = TestContext.Current.CancellationToken;
        var (service, operations) = Services();
        await operations.CreateProfileAsync(Spec("editable"), token);
        var item = Assert.Single(await service.GetConnectionsAsync(token));
        var original = await operations.ReadProfileTextAsync(item.ProfileName, item.ProfilePath, token);
        var updated = original.Replace(TunnelId, "tunnel_11111111111111111111111111111111", StringComparison.Ordinal)
            .Replace("64343/stream", "7555/mcp", StringComparison.Ordinal);

        await operations.SaveProfileTextAsync(item.ProfileName, item.ProfilePath, updated, token);
        var saved = await operations.ReadProfileTextAsync(item.ProfileName, item.ProfilePath, token);
        Assert.Contains("tunnel_11111111111111111111111111111111", saved, StringComparison.Ordinal);
        Assert.Contains("7555/mcp", saved, StringComparison.Ordinal);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            operations.SaveProfileTextAsync(item.ProfileName, item.ProfilePath, "config_version: 1\ncontrol_plane: {}\n", token));
        Assert.Contains("profile validation failed", error.Message, StringComparison.OrdinalIgnoreCase);
        var afterFailure = await operations.ReadProfileTextAsync(item.ProfileName, item.ProfilePath, token);
        Assert.Equal(saved, afterFailure);
    }

    [Fact]
    public async Task ProfileDeleteRevalidatesOfficialPathBeforeDeleting()
    {
        var token = TestContext.Current.CancellationToken;
        var (service, operations) = Services();
        await operations.CreateProfileAsync(Spec("delete-me"), token);
        var item = Assert.Single(await service.GetConnectionsAsync(token));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            operations.DeleteProfileAsync(item.ProfileName, Path.Combine(_root, "wrong.yaml"), token));
        Assert.True(File.Exists(item.ProfilePath));

        await operations.DeleteProfileAsync(item.ProfileName, item.ProfilePath, token);
        Assert.False(File.Exists(item.ProfilePath));
        Assert.Empty(await service.GetConnectionsAsync(token));
    }

    [Fact]
    public async Task NonZeroJsonStaleStatusIsPreserved()
    {
        var token = TestContext.Current.CancellationToken;
        var (service, operations) = Services();
        await operations.CreateProfileAsync(Spec("stale-profile"), token);
        var profile = Assert.Single(await service.GetConnectionsAsync(token));
        var seed = RuntimeSeed(profile, "stale-one");
        await operations.StartAsync(seed, "secret", token);
        SetEnvironment("FAKE_STALE_STATUS", "stale-one");

        var current = (await service.GetConnectionsAsync(token)).Single(item => item.RuntimeAlias == "stale-one");
        Assert.Equal(RuntimeState.Stale, current.State);
        Assert.Contains("remote tunnel not found", current.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingLogFileProducesClearError()
    {
        var token = TestContext.Current.CancellationToken;
        var (_, operations) = Services();
        var missing = Path.Combine(_root, "missing.log");
        var exception = await Assert.ThrowsAsync<FileNotFoundException>(() => operations.ReadLogTailAsync(missing, cancellationToken: token));
        Assert.Contains("日志文件不存在", exception.Message, StringComparison.Ordinal);
    }

    private (TunnelClientService Service, TunnelClientOperations Operations) Services()
    {
        var options = new TunnelClientOptions { ExecutablePath = FakeExecutable };
        var service = new TunnelClientService(options);
        return (service, new TunnelClientOperations(options, service));
    }

    private static ProfileSpec Spec(string name, McpType type = McpType.Http) =>
        new(name, TunnelId, type, type == McpType.Http ? "http://127.0.0.1:64343/stream" : "dotnet mcp-server.dll");

    private static TunnelConnection RuntimeSeed(TunnelConnection profile, string alias) => profile with
    {
        Name = alias,
        RuntimeAlias = alias,
        RuntimeProfileName = profile.ProfileName,
        RuntimeProfilePath = profile.ProfilePath,
        State = RuntimeState.Stopped,
        ProcessRunning = false,
        Healthy = false,
        Ready = false
    };

    private static string FakeExecutable
    {
        get
        {
            var directory = Path.GetDirectoryName(typeof(Marker).Assembly.Location)
                ?? throw new InvalidOperationException("无法确定 fake tunnel-client 输出目录");
            var path = Path.Combine(directory, "OpenAITunnelManager.FakeTunnelClient.exe");
            if (!File.Exists(path)) throw new FileNotFoundException("fake tunnel-client apphost 未复制到测试输出目录", path);
            return path;
        }
    }

    private void SetEnvironment(string name, string? value)
    {
        if (!_previousEnvironment.ContainsKey(name)) _previousEnvironment[name] = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose()
    {
        foreach (var pair in _previousEnvironment) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        try { Directory.Delete(_root, recursive: true); } catch { }
    }
}
