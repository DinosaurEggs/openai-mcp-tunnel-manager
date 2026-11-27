using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using OpenAITunnelManager.Infrastructure.Updates;
using Xunit;

namespace OpenAITunnelManager.Tests;

public sealed class ManagerUpdateTests
{
    [Theory]
    [InlineData("1.3.0", false)]
    [InlineData("1.2.0", false)]
    [InlineData("1.4.0", true)]
    public async Task ChecksReleaseVersionAndSelectsMatchingArchitecture(string latest, bool available)
    {
        var json = JsonSerializer.Serialize(new
        {
            tag_name = "v" + latest,
            body = "Release notes",
            assets = new[]
            {
                new { name = "OpenAITunnelManager-win-x64.zip", browser_download_url = "https://example.test/x64.zip", digest = "sha256:test" },
                new { name = "OpenAITunnelManager-win-arm64.zip", browser_download_url = "https://example.test/arm64.zip", digest = "sha256:test" }
            }
        });
        using var http = new HttpClient(new StubHandler(_ => new StringContent(json)));
        var result = await new ManagerUpdateService(http).CheckAsync(new Version(1, 3, 0, 0), TestContext.Current.CancellationToken);
        if (!available)
        {
            Assert.Null(result);
            return;
        }
        Assert.NotNull(result);
        Assert.Equal(new Version(latest), result.Version);
        Assert.Equal("Release notes", result.Notes);
        Assert.Equal(RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "/arm64.zip" : "/x64.zip", result.DownloadUri.AbsolutePath);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PreparesPackageOnlyWhenDigestMatches(bool valid)
    {
        using var temp = new TempDirectory();
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(archive.CreateEntry("OpenAITunnelManager.exe").Open()))
            writer.Write("new executable");
        var bytes = stream.ToArray();
        var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(valid ? bytes : new byte[] { 0 }));
        using var http = new HttpClient(new StubHandler(_ => new ByteArrayContent(bytes)));
        var service = new ManagerUpdateService(http);
        var release = new ManagerRelease(new Version(1, 4, 0), "", new Uri("https://example.test/release.zip"), digest);
        var stage = Path.Combine(temp.Path, "stage");
        if (valid)
        {
            await service.PrepareAsync(release, stage, token: TestContext.Current.CancellationToken);
            Assert.Equal("new executable", File.ReadAllText(Path.Combine(stage, "payload", "OpenAITunnelManager.exe")));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => service.PrepareAsync(release, stage, token: TestContext.Current.CancellationToken));
            Assert.False(Directory.Exists(stage));
        }
    }

    [Fact]
    public void InstallationPreservesPortableData()
    {
        using var temp = new TempDirectory();
        var root = Path.Combine(temp.Path, "app");
        var payload = Path.Combine(temp.Path, "payload");
        Write(root, "OpenAITunnelManager.exe", "old");
        Write(payload, "OpenAITunnelManager.exe", "new");
        foreach (var directory in new[] { "config", "logs", "state", "tunnel-client" })
        {
            Write(root, Path.Combine(directory, "data"), "existing");
            Write(payload, Path.Combine(directory, "data"), "package");
        }

        ManagerUpdateInstaller.Apply(root, payload, Path.Combine(temp.Path, "backup"));
        Assert.Equal("new", File.ReadAllText(Path.Combine(root, "OpenAITunnelManager.exe")));
        foreach (var directory in new[] { "config", "logs", "state", "tunnel-client" })
            Assert.Equal("existing", File.ReadAllText(Path.Combine(root, directory, "data")));
    }

    [Fact]
    public void InstallationFailureRestoresPreviouslyReplacedFiles()
    {
        using var temp = new TempDirectory();
        var root = Path.Combine(temp.Path, "app");
        var payload = Path.Combine(temp.Path, "payload");
        Write(root, "a.dll", "old");
        Write(payload, "a.dll", "new");
        Write(payload, "z.dll", "new");
        Directory.CreateDirectory(Path.Combine(root, "z.dll"));

        Assert.ThrowsAny<Exception>(() => ManagerUpdateInstaller.Apply(root, payload, Path.Combine(temp.Path, "backup")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(root, "a.dll")));
        Assert.True(Directory.Exists(Path.Combine(root, "z.dll")));
    }

    private static void Write(string root, string path, string content)
    {
        var file = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpContent> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = response(request) });
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ManagerUpdateTests", Guid.NewGuid().ToString("N"));
        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
