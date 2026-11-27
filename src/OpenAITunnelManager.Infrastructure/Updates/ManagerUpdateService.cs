using System.Net.Http.Headers;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace OpenAITunnelManager.Infrastructure.Updates;

public sealed record ManagerRelease(Version Version, string Notes, Uri DownloadUri, string Digest);

public sealed class ManagerUpdateService(HttpClient httpClient)
{
    private const string LatestRelease = "https://api.github.com/repos/DinosaurEggs/openai-mcp-tunnel-manager/releases/latest";

    public async Task<ManagerRelease?> CheckAsync(Version currentVersion, CancellationToken token = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestRelease);
        request.Headers.UserAgent.ParseAdd("OpenAITunnelManager/" + currentVersion.ToString(3));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await httpClient.SendAsync(request, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString();
        if (!Version.TryParse(tag?.TrimStart('v'), out var version))
            throw new InvalidDataException($"无法识别正式发布版本：{tag}");
        var current = new Version(currentVersion.Major, currentVersion.Minor, Math.Max(currentVersion.Build, 0));
        if (version <= current) return null;

        var assetName = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "OpenAITunnelManager-win-x64.zip",
            Architecture.Arm64 => "OpenAITunnelManager-win-arm64.zip",
            _ => throw new PlatformNotSupportedException("当前架构没有 Manager 更新包")
        };
        var asset = root.GetProperty("assets").EnumerateArray().FirstOrDefault(item =>
            item.GetProperty("name").GetString() == assetName);
        if (asset.ValueKind == JsonValueKind.Undefined)
            throw new InvalidDataException($"正式版本缺少 {assetName}");
        var uri = new Uri(asset.GetProperty("browser_download_url").GetString()!);
        var digest = asset.TryGetProperty("digest", out var hash) ? hash.GetString() ?? string.Empty : string.Empty;
        var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty;
        return new ManagerRelease(version, notes, uri, digest);
    }

    public async Task PrepareAsync(ManagerRelease release, string directory, IProgress<int?>? progress = null,
        CancellationToken token = default)
    {
        Directory.CreateDirectory(directory);
        var archive = Path.Combine(directory, "release.zip");
        var payload = Path.Combine(directory, "payload");
        try
        {
            using (var response = await httpClient.GetAsync(release.DownloadUri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var length = response.Content.Headers.ContentLength;
                await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                await using var destination = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true);
                var buffer = new byte[128 * 1024];
                long downloaded = 0;
                var lastPercent = -1;
                progress?.Report(null);
                while (true)
                {
                    var count = await source.ReadAsync(buffer, token).ConfigureAwait(false);
                    if (count == 0) break;
                    await destination.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                    downloaded += count;
                    if (length is > 0)
                    {
                        var percent = (int)Math.Clamp(downloaded * 100d / length.Value, 0d, 100d);
                        if (percent != lastPercent) progress?.Report(lastPercent = percent);
                    }
                }
            }
            if (!string.IsNullOrWhiteSpace(release.Digest))
            {
                string hash;
                await using (var file = File.OpenRead(archive))
                    hash = Convert.ToHexString(await SHA256.HashDataAsync(file, token).ConfigureAwait(false));
                if (!string.Equals("sha256:" + hash, release.Digest, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Manager 更新包 SHA-256 校验失败");
            }

            await Task.Run(() => ZipFile.ExtractToDirectory(archive, payload), token).ConfigureAwait(false);
            if (!File.Exists(Path.Combine(payload, "OpenAITunnelManager.exe")))
                throw new InvalidDataException("更新包缺少 OpenAITunnelManager.exe");
            File.Delete(archive);
        }
        catch
        {
            try { Directory.Delete(directory, true); } catch { }
            throw;
        }
    }
}
