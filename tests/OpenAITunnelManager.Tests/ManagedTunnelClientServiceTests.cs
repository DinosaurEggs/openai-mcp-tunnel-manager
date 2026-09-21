using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using OpenAITunnelManager.Infrastructure.Settings;
using OpenAITunnelManager.Infrastructure.TunnelClient;
using Xunit;

namespace OpenAITunnelManager.Tests;

public sealed class ManagedTunnelClientServiceTests
{
    [Theory]
    [InlineData(Architecture.X64, "amd64")]
    [InlineData(Architecture.Arm64, "arm64")]
    public void MapsSupportedWindowsArchitectures(Architecture architecture, string expected)
    {
        Assert.Equal(expected, ManagedTunnelClientService.GetWindowsAssetArchitecture(architecture));
    }

    [Fact]
    public async Task LatestReleaseSelectsExactWindowsAssetAndVerifiesManifest()
    {
        var token = TestContext.Current.CancellationToken;
        using var temp = new TempDirectory();
        const string version = "v9.8.7";
        const string hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var architecture = ManagedTunnelClientService.GetWindowsAssetArchitecture(RuntimeInformation.ProcessArchitecture);
        var assetName = $"tunnel-client-{version}-windows-{architecture}.zip";
        var latestJson = $$"""
            {
              "tag_name": "{{version}}",
              "draft": false,
              "prerelease": false,
              "assets": [
                {
                  "name": "tunnel-client-runtime-cloudflared-{{version}}-windows-{{architecture}}.zip",
                  "browser_download_url": "https://example.test/wrong.zip",
                  "digest": "sha256:ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"
                },
                {
                  "name": "{{assetName}}",
                  "browser_download_url": "https://example.test/client.zip",
                  "digest": "sha256:{{hash}}"
                },
                {
                  "name": "SHA256SUMS.txt",
                  "browser_download_url": "https://example.test/SHA256SUMS.txt"
                }
              ]
            }
            """;

        using var client = new HttpClient(new StubHandler(request =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            if (url.EndsWith("/releases/latest", StringComparison.Ordinal))
                return Json(latestJson);
            if (url.EndsWith("/SHA256SUMS.txt", StringComparison.Ordinal))
                return Text($"{hash}  {assetName}\n");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));

        using var service = new ManagedTunnelClientService(client, new AppDataPaths(temp.Path));
        var release = await service.GetLatestReleaseAsync(token);

        Assert.Equal(version, release.Version);
        Assert.Equal(assetName, release.AssetName);
        Assert.Equal("https://example.test/client.zip", release.DownloadUri.AbsoluteUri);
        Assert.Equal(hash, release.Sha256);
    }

    [Fact]
    public async Task LatestReleaseRejectsDigestManifestMismatch()
    {
        var token = TestContext.Current.CancellationToken;
        using var temp = new TempDirectory();
        const string version = "v1.0.0";
        const string digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string manifest = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var architecture = ManagedTunnelClientService.GetWindowsAssetArchitecture(RuntimeInformation.ProcessArchitecture);
        var assetName = $"tunnel-client-{version}-windows-{architecture}.zip";
        var latestJson = $$"""
            {
              "tag_name": "{{version}}",
              "draft": false,
              "prerelease": false,
              "assets": [
                {
                  "name": "{{assetName}}",
                  "browser_download_url": "https://example.test/client.zip",
                  "digest": "sha256:{{digest}}"
                },
                {
                  "name": "SHA256SUMS.txt",
                  "browser_download_url": "https://example.test/SHA256SUMS.txt"
                }
              ]
            }
            """;

        using var client = new HttpClient(new StubHandler(request =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            if (url.EndsWith("/releases/latest", StringComparison.Ordinal))
                return Json(latestJson);
            if (url.EndsWith("/SHA256SUMS.txt", StringComparison.Ordinal))
                return Text($"{manifest}  {assetName}\n");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));

        using var service = new ManagedTunnelClientService(client, new AppDataPaths(temp.Path));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.GetLatestReleaseAsync(token));
        Assert.Contains("不一致", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CleanupDownloadedVersionsKeepsCurrentVersionOnly()
    {
        using var temp = new TempDirectory();
        var paths = new AppDataPaths(temp.Path);
        foreach (var version in new[] { "v1.0.0", "v1.1.0", "v1.2.0" })
        {
            Directory.CreateDirectory(paths.GetManagedTunnelClientVersionDirectory(version));
        }

        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        using var service = new ManagedTunnelClientService(client, paths);

        var deleted = service.CleanupDownloadedVersions("v1.1.0");

        Assert.Equal(2, deleted);
        Assert.False(Directory.Exists(paths.GetManagedTunnelClientVersionDirectory("v1.0.0")));
        Assert.True(Directory.Exists(paths.GetManagedTunnelClientVersionDirectory("v1.1.0")));
        Assert.False(Directory.Exists(paths.GetManagedTunnelClientVersionDirectory("v1.2.0")));
    }

    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(text, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage Text(string text) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(text, Encoding.UTF8, "text/plain")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "OpenAITunnelManager.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
