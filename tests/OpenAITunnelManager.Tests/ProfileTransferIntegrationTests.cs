using OpenAITunnelManager.FakeTunnelClient;
using OpenAITunnelManager.Infrastructure.TunnelClient;
using Xunit;

namespace OpenAITunnelManager.Tests;

public sealed class ProfileTransferIntegrationTests : IDisposable
{
    private const string TunnelId = "tunnel_0123456789abcdef0123456789abcdef";
    private readonly string _root;
    private readonly Dictionary<string, string?> _previousEnvironment = new(StringComparer.Ordinal);

    public ProfileTransferIntegrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "OpenAITunnelManager.ProfileTransfer", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        SetEnvironment("FAKE_TUNNEL_STATE", Path.Combine(_root, "state.json"));
        SetEnvironment("FAKE_PROFILE_DIR", Path.Combine(_root, "profiles"));
        SetEnvironment("FAKE_RUNTIME_DIR", Path.Combine(_root, "runtime"));
    }

    [Fact]
    public async Task ImportUsesOfficialProfilesAddAndExportRevalidatesListedPath()
    {
        var token = TestContext.Current.CancellationToken;
        var options = new TunnelClientOptions { ExecutablePath = FakeExecutable };
        var inventory = new TunnelClientService(options);
        var operations = new TunnelClientOperations(options, inventory);
        var source = Path.Combine(_root, "source.yaml");
        var sourceText = $"config_version: 1\ncontrol_plane:\n  tunnel_id: {TunnelId}\n  api_key: env:CONTROL_PLANE_API_KEY\nmcp:\n  server_urls:\n    - channel: main\n      url: http://127.0.0.1:64343/stream\n";
        await File.WriteAllTextAsync(source, sourceText, token);

        await operations.ImportProfileAsync("imported", source, token);
        var item = Assert.Single(await inventory.GetConnectionsAsync(token));
        Assert.Equal("profile:imported", item.Identity);
        Assert.True(item.ProfileListed);

        var exported = Path.Combine(_root, "exported.yaml");
        await operations.ExportProfileAsync(item.ProfileName, item.ProfilePath, exported, token);
        Assert.Equal(await File.ReadAllTextAsync(item.ProfilePath, token), await File.ReadAllTextAsync(exported, token));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            operations.ExportProfileAsync(item.ProfileName, Path.Combine(_root, "wrong.yaml"), Path.Combine(_root, "unsafe.yaml"), token));
        Assert.False(File.Exists(Path.Combine(_root, "unsafe.yaml")));
    }

    [Fact]
    public async Task ImportRejectsInvalidProfileNameBeforeCallingCli()
    {
        var token = TestContext.Current.CancellationToken;
        var options = new TunnelClientOptions { ExecutablePath = FakeExecutable };
        var inventory = new TunnelClientService(options);
        var operations = new TunnelClientOperations(options, inventory);
        var source = Path.Combine(_root, "source.yaml");
        await File.WriteAllTextAsync(source, $"control_plane:\n  tunnel_id: {TunnelId}\nmcp:\n  server_urls:\n    - url: http://127.0.0.1:1\n", token);

        await Assert.ThrowsAsync<ArgumentException>(() => operations.ImportProfileAsync("bad name", source, token));
        Assert.Empty(await inventory.GetConnectionsAsync(token));
    }

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
