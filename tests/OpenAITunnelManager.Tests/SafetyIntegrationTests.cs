using System.Diagnostics;
using System.Text.Json;
using OpenAITunnelManager.Core.Models;
using OpenAITunnelManager.FakeTunnelClient;
using OpenAITunnelManager.Infrastructure.TunnelClient;
using Xunit;

namespace OpenAITunnelManager.Tests;

public sealed class SafetyIntegrationTests : IDisposable
{
    private const string TunnelId = "tunnel_0123456789abcdef0123456789abcdef";
    private readonly string _root;
    private readonly Dictionary<string, string?> _previousEnvironment = new(StringComparer.Ordinal);

    public SafetyIntegrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "OpenAITunnelManager.Safety", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        SetEnvironment("FAKE_TUNNEL_STATE", StatePath);
        SetEnvironment("FAKE_PROFILE_DIR", Path.Combine(_root, "profiles"));
        SetEnvironment("FAKE_RUNTIME_DIR", Path.Combine(_root, "runtime"));
        SetEnvironment("FAKE_FAIL_STOP", null);
    }

    [Fact]
    public async Task DeletingForegroundProfileStopsOwnedProcessFirst()
    {
        var token = TestContext.Current.CancellationToken;
        var (service, operations) = Services();
        await operations.CreateProfileAsync(Spec("foreground-delete"), token);
        var profile = Assert.Single(await service.GetConnectionsAsync(token));
        await operations.StartAsync(profile, "secret", token);
        var running = await operations.GetStatusAsync(profile, token);
        Assert.True(running.ProcessRunning);
        Assert.NotNull(running.ProcessId);
        var pid = running.ProcessId!.Value;

        await operations.DeleteProfileAsync(profile.ProfileName, profile.ProfilePath, token);

        Assert.False(File.Exists(profile.ProfilePath));
        await Task.Delay(150, token);
        Assert.False(IsProcessAlive(pid));
        Assert.Empty(await service.GetConnectionsAsync(token));
    }

    [Fact]
    public async Task RestartDoesNotReconnectWhenStopFails()
    {
        var token = TestContext.Current.CancellationToken;
        var (service, operations) = Services();
        await operations.CreateProfileAsync(Spec("restart"), token);
        var profile = Assert.Single(await service.GetConnectionsAsync(token));
        var seed = profile with
        {
            Name = "restart-runtime",
            RuntimeAlias = "restart-runtime",
            RuntimeProfileName = profile.ProfileName,
            RuntimeProfilePath = profile.ProfilePath,
            State = RuntimeState.Stopped
        };
        await operations.StartAsync(seed, "secret", token);
        Assert.Equal(1, ReadConnectCount("restart-runtime"));
        var running = (await service.GetConnectionsAsync(token)).Single(item => item.RuntimeAlias == "restart-runtime");

        SetEnvironment("FAKE_FAIL_STOP", "restart-runtime");
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => operations.RestartAsync(running, "secret", token));

        Assert.Contains("stop failed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, ReadConnectCount("restart-runtime"));
        var after = await service.GetStatusAsync(running, token);
        Assert.True(after.ProcessRunning);
    }

    private int ReadConnectCount(string alias)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(StatePath));
        return document.RootElement.GetProperty("runtimes").GetProperty(alias).GetProperty("connect_count").GetInt32();
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private (TunnelClientService Service, TunnelClientOperations Operations) Services()
    {
        var options = new TunnelClientOptions { ExecutablePath = FakeExecutable };
        var service = new TunnelClientService(options);
        return (service, new TunnelClientOperations(options, service));
    }

    private static ProfileSpec Spec(string name) => new(name, TunnelId, McpType.Http, "http://127.0.0.1:64343/stream");

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

    private string StatePath => Path.Combine(_root, "state.json");

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
