using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace OpenAITunnelManager.Infrastructure.TunnelClient;

public sealed record TunnelClientUpdateResult(
    string Version,
    string ExecutablePath,
    bool Updated,
    string Message);

public sealed class TunnelClientUpdateService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/openai/tunnel-client/releases/latest";
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(30);
    private readonly HttpClient _httpClient;

    public TunnelClientUpdateService()
    {
        _httpClient = new HttpClient
        {
            Timeout = HttpTimeout
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("OpenAITunnelManager", "1.0"));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public static string ManagedExecutablePath =>
        Path.Combine(AppContext.BaseDirectory, "tunnel-client.exe");

    public static string ManagedVersionPath =>
        Path.Combine(AppContext.BaseDirectory, "tunnel-client.version");

    public async Task<TunnelClientUpdateResult> EnsureLatestAsync(
        bool forceDownload = false,
        CancellationToken cancellationToken = default)
    {
        var release = await GetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
        var installedVersion = ReadInstalledVersion();

        if (!forceDownload &&
            File.Exists(ManagedExecutablePath) &&
            string.Equals(installedVersion, release.Version, StringComparison.OrdinalIgnoreCase))
        {
            return new TunnelClientUpdateResult(
                release.Version,
                ManagedExecutablePath,
                false,
                $"tunnel-client 已是最新版 {release.Version}");
        }

        await DownloadAndInstallAsync(release, cancellationToken).ConfigureAwait(false);

        return new TunnelClientUpdateResult(
            release.Version,
            ManagedExecutablePath,
            true,
            $"已安装 tunnel-client {release.Version}");
    }

    public string ReadInstalledVersion()
    {
        try
        {
            return File.Exists(ManagedVersionPath)
                ? File.ReadAllText(ManagedVersionPath).Trim()
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private async Task<TunnelClientRelease> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        await using var stream = await _httpClient.GetStreamAsync(LatestReleaseApi, cancellationToken)
            .ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var root = document.RootElement;
        var version = root.GetProperty("tag_name").GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new InvalidOperationException("GitHub 最新 release 缺少 tag_name");
        }

        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "amd64",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException(
                $"当前架构 {RuntimeInformation.ProcessArchitecture} 不受官方 tunnel-client Windows 发布支持")
        };

        var expectedName = $"tunnel-client-{version}-windows-{architecture}.zip";
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            if (!string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var downloadUrl = asset.GetProperty("browser_download_url").GetString();
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                break;
            }

            var digest = asset.TryGetProperty("digest", out var digestElement)
                ? digestElement.GetString()
                : null;

            return new TunnelClientRelease(
                version,
                expectedName,
                downloadUrl,
                NormalizeSha256Digest(digest));
        }

        throw new InvalidOperationException(
            $"GitHub release {version} 中未找到 {expectedName}");
    }

    private async Task DownloadAndInstallAsync(
        TunnelClientRelease release,
        CancellationToken cancellationToken)
    {
        var baseDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        Directory.CreateDirectory(baseDirectory);

        var workDirectory = Path.Combine(
            baseDirectory,
            $".tunnel-client-update-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDirectory);

        var zipPath = Path.Combine(workDirectory, release.AssetName);
        var extractDirectory = Path.Combine(workDirectory, "extract");
        var stagedExecutable = Path.Combine(baseDirectory, "tunnel-client.exe.new");

        try
        {
            using (var response = await _httpClient.GetAsync(
                       release.DownloadUrl,
                       HttpCompletionOption.ResponseHeadersRead,
                       cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
                await using var destination = new FileStream(
                    zipPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(release.Sha256))
            {
                var actual = await ComputeSha256Async(zipPath, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(actual, release.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"tunnel-client 下载校验失败：期望 SHA-256 {release.Sha256}，实际 {actual}");
                }
            }

            ZipFile.ExtractToDirectory(zipPath, extractDirectory);
            var extractedExecutable = Directory
                .EnumerateFiles(extractDirectory, "tunnel-client.exe", SearchOption.AllDirectories)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(extractedExecutable))
            {
                throw new InvalidDataException(
                    $"{release.AssetName} 中未找到 tunnel-client.exe");
            }

            File.Copy(extractedExecutable, stagedExecutable, overwrite: true);
            File.Move(stagedExecutable, ManagedExecutablePath, overwrite: true);
            await File.WriteAllTextAsync(
                ManagedVersionPath,
                release.Version + Environment.NewLine,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDeleteFile(stagedExecutable);
            TryDeleteDirectory(workDirectory);
        }
    }

    private static string? NormalizeSha256Digest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest))
        {
            return null;
        }

        const string prefix = "sha256:";
        return digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? digest[prefix.Length..].Trim()
            : digest.Trim();
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private sealed record TunnelClientRelease(
        string Version,
        string AssetName,
        string DownloadUrl,
        string? Sha256);
}
