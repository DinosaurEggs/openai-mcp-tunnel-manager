using System.Net;
using System.Net.Sockets;
using System.Text;
using OpenAITunnelManager.Core.Abstractions;
using OpenAITunnelManager.Core.Models;
using OpenAITunnelManager.Infrastructure.TunnelClient;
using Xunit;

namespace OpenAITunnelManager.Tests;

public sealed class HealthCompatibilityTests
{
    [Fact]
    public async Task OldRuntimeWithoutAdvertisedDetailedEndpointsDoesNotProbeNewRoutes()
    {
        var token = TestContext.Current.CancellationToken;
        var connection = RuntimeConnection() with
        {
            HealthUrl = "http://127.0.0.1:54321/healthz",
            HealthDetailsUrl = string.Empty,
            McpHealthUrl = string.Empty,
            Healthy = true,
            Ready = true
        };
        var operations = Operations(connection);

        var text = await operations.GetDetailedHealthAsync(connection, token);

        Assert.Equal("未声明详细健康接口", text);
    }

    [Fact]
    public async Task AdvertisedLoopbackDetailedEndpointsAreRead()
    {
        var token = TestContext.Current.CancellationToken;
        await using var server = await LoopbackHttpServer.StartAsync(2, token);
        var baseUrl = $"http://127.0.0.1:{server.Port}";
        var connection = RuntimeConnection() with
        {
            HealthDetailsUrl = baseUrl + "/custom-details",
            McpHealthUrl = baseUrl + "/custom-mcp"
        };
        var operations = Operations(connection);

        var text = await operations.GetDetailedHealthAsync(connection, token);

        Assert.Contains("Health details", text, StringComparison.Ordinal);
        Assert.Contains("custom-details", text, StringComparison.Ordinal);
        Assert.Contains("MCP health", text, StringComparison.Ordinal);
        Assert.Contains("custom-mcp", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonLoopbackDetailedEndpointIsRejectedWithoutRequest()
    {
        var token = TestContext.Current.CancellationToken;
        var connection = RuntimeConnection() with
        {
            HealthDetailsUrl = "https://example.com/health",
            McpHealthUrl = string.Empty
        };
        var operations = Operations(connection);

        var text = await operations.GetDetailedHealthAsync(connection, token);

        Assert.Contains("拒绝访问非 loopback Health URL", text, StringComparison.Ordinal);
    }

    private static TunnelClientOperations Operations(TunnelConnection connection) =>
        new(new TunnelClientOptions(), new StaticInventory(connection));

    private static TunnelConnection RuntimeConnection() => new(
        Name: "idea",
        ProfileName: "idea",
        ProfilePath: @"C:\profiles\idea.yaml",
        ProfileListed: true,
        RuntimeAlias: "idea",
        RuntimeProfileName: "idea",
        RuntimeProfilePath: @"C:\profiles\idea.yaml",
        TunnelId: "tunnel_0123456789abcdef0123456789abcdef",
        TargetKind: "server_url",
        TargetValue: "http://127.0.0.1:64343/stream",
        State: RuntimeState.Ready,
        ProcessRunning: true,
        Healthy: true,
        Ready: true,
        HealthUrl: "http://127.0.0.1:54321/healthz",
        HealthDetailsUrl: string.Empty,
        McpHealthUrl: string.Empty,
        LogPath: string.Empty,
        ProcessId: 1,
        Error: string.Empty);

    private sealed class StaticInventory(TunnelConnection connection) : ITunnelClientService
    {
        public Task<string> GetVersionAsync(CancellationToken cancellationToken = default) => Task.FromResult("fake");
        public Task<IReadOnlyList<TunnelConnection>> GetConnectionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TunnelConnection>>([connection]);
        public Task<TunnelConnection> GetStatusAsync(TunnelConnection item, CancellationToken cancellationToken = default) =>
            Task.FromResult(connection);
        public Task StopRuntimeAsync(string alias, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class LoopbackHttpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task _serveTask;
        private readonly CancellationTokenSource _cts = new();

        private LoopbackHttpServer(TcpListener listener, int requests)
        {
            _listener = listener;
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _serveTask = ServeAsync(requests, _cts.Token);
        }

        public int Port { get; }

        public static Task<LoopbackHttpServer> StartAsync(int requests, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return Task.FromResult(new LoopbackHttpServer(listener, requests));
        }

        private async Task ServeAsync(int requests, CancellationToken cancellationToken)
        {
            for (var index = 0; index < requests; index++)
            {
                using var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync(cancellationToken) ?? string.Empty;
                var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var path = parts.Length > 1 ? parts[1] : "/";
                string? line;
                do { line = await reader.ReadLineAsync(cancellationToken); } while (!string.IsNullOrEmpty(line));
                var body = Encoding.UTF8.GetBytes($"{{\"ok\":true,\"path\":\"{path}\"}}");
                var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header, cancellationToken);
                await stream.WriteAsync(body, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            try { await _serveTask; } catch (OperationCanceledException) { } catch (SocketException) { }
            _cts.Dispose();
        }
    }
}
