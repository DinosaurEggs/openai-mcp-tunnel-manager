using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using OpenAITunnelManager.Infrastructure.Settings;

namespace OpenAITunnelManager.Infrastructure.TunnelClient;

public sealed record TunnelClientReleaseInfo(
    string Version,
    string AssetName,
    Uri DownloadUri,
    string Sha256);

public sealed record ManagedTunnelClientInstallResult(
    string Version,
    string ExecutablePath,
    bool Updated,
    string Message);

public enum ManagedTunnelClientProgressStage
{
    Checking,
    Downloading,
    Verifying,
    Installing
}

public sealed record ManagedTunnelClientProgress(
    ManagedTunnelClientProgressStage Stage,
    string Message,
    long BytesReceived = 0,
    long? TotalBytes = null);

public sealed record TunnelClientValidationResult(
    string ExecutablePath,
    string VersionText);

public sealed class ManagedTunnelClientService : IDisposable
{
    private const string LatestReleaseApi = "https://api.github.com/repos/openai/tunnel-client/releases/latest";
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ValidationTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _httpClient;
    private readonly AppDataPaths _paths;
    private readonly bool _ownsHttpClient;

    public ManagedTunnelClientService()
        : this(CreateHttpClient(), AppDataPaths.Current, ownsHttpClient: true)
    {
    }

    public ManagedTunnelClientService(HttpClient httpClient, AppDataPaths paths)
        : this(httpClient, paths, ownsHttpClient: false)
    {
    }

    private ManagedTunnelClientService(HttpClient httpClient, AppDataPaths paths, bool ownsHttpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _ownsHttpClient = ownsHttpClient;
        EnsureHeaders(_httpClient);
    }

    public string GetExecutablePath(string version) =>
        _paths.GetManagedTunnelClientExecutablePath(version);

    public bool IsInstalled(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return false;
        try
        {
            return File.Exists(GetExecutablePath(version));
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    public async Task<TunnelClientReleaseInfo> GetLatestReleaseAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            LatestReleaseApi,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;

        if (ReadBoolean(root, "draft") || ReadBoolean(root, "prerelease"))
        {
            throw new InvalidDataException("GitHub latest release 不是正式版本");
        }

        var version = ReadString(root, "tag_name");
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new InvalidDataException("GitHub latest release 缺少 tag_name");
        }

        var architecture = GetWindowsAssetArchitecture(RuntimeInformation.ProcessArchitecture);
        var assetName = $"tunnel-client-{version}-windows-{architecture}.zip";
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"GitHub release {version} 缺少 assets");
        }

        JsonElement? archiveAsset = null;
        JsonElement? checksumAsset = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = ReadString(asset, "name");
            if (string.Equals(name, assetName, StringComparison.OrdinalIgnoreCase))
                archiveAsset = asset.Clone();
            else if (string.Equals(name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
                checksumAsset = asset.Clone();
        }

        if (archiveAsset is null)
        {
            throw new InvalidDataException($"GitHub release {version} 中未找到 {assetName}");
        }

        var downloadUrl = ReadString(archiveAsset.Value, "browser_download_url");
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var downloadUri) ||
            downloadUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException($"{assetName} 的下载地址无效");
        }

        var assetDigest = NormalizeSha256Digest(ReadString(archiveAsset.Value, "digest"));
        string? manifestDigest = null;
        if (checksumAsset is not null)
        {
            var checksumUrl = ReadString(checksumAsset.Value, "browser_download_url");
            if (Uri.TryCreate(checksumUrl, UriKind.Absolute, out var checksumUri) &&
                checksumUri.Scheme == Uri.UriSchemeHttps)
            {
                manifestDigest = await ReadChecksumFromManifestAsync(
                    checksumUri,
                    assetName,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        if (assetDigest is not null &&
            manifestDigest is not null &&
            !string.Equals(assetDigest, manifestDigest, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"GitHub release {version} 的 digest 与 SHA256SUMS.txt 不一致");
        }

        var checksum = assetDigest ?? manifestDigest;
        if (checksum is null)
        {
            throw new InvalidDataException(
                $"GitHub release {version} 没有可验证的 SHA-256 校验值");
        }

        return new TunnelClientReleaseInfo(version, assetName, downloadUri, checksum);
    }

    public async Task<ManagedTunnelClientInstallResult> InstallLatestAsync(
        string currentVersion,
        bool forceDownload = false,
        CancellationToken cancellationToken = default,
        IProgress<ManagedTunnelClientProgress>? progress = null)
    {
        progress?.Report(new ManagedTunnelClientProgress(
            ManagedTunnelClientProgressStage.Checking,
            "正在检查官方最新版本…"));
        var release = await GetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
        var executablePath = GetExecutablePath(release.Version);

        if (!forceDownload &&
            string.Equals(currentVersion?.Trim(), release.Version, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(executablePath))
        {
            return new ManagedTunnelClientInstallResult(
                release.Version,
                executablePath,
                false,
                $"tunnel-client 已是最新版 {release.Version}");
        }

        if (!forceDownload && File.Exists(executablePath))
        {
            _ = await ValidateExecutableAsync(executablePath, cancellationToken).ConfigureAwait(false);
            return new ManagedTunnelClientInstallResult(
                release.Version,
                executablePath,
                !string.Equals(currentVersion?.Trim(), release.Version, StringComparison.OrdinalIgnoreCase),
                $"已切换到已下载的 tunnel-client {release.Version}");
        }

        await DownloadAndInstallAsync(release, progress, cancellationToken).ConfigureAwait(false);
        return new ManagedTunnelClientInstallResult(
            release.Version,
            executablePath,
            true,
            $"已安装 tunnel-client {release.Version}");
    }

    public async Task<TunnelClientValidationResult> ValidateCustomExecutableAsync(
        string executablePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new FileNotFoundException("未选择 tunnel-client.exe");

        var fullPath = Path.GetFullPath(executablePath.Trim());
        var version = await ValidateExecutableAsync(fullPath, cancellationToken).ConfigureAwait(false);
        return new TunnelClientValidationResult(fullPath, version);
    }

    public int CleanupDownloadedVersions(string currentVersion)
    {
        if (string.IsNullOrWhiteSpace(currentVersion) ||
            !Directory.Exists(_paths.ManagedTunnelClientVersionsDirectory))
        {
            return 0;
        }

        string currentDirectory;
        try
        {
            currentDirectory = Path.GetFullPath(_paths.GetManagedTunnelClientVersionDirectory(currentVersion));
        }
        catch (InvalidDataException)
        {
            return 0;
        }

        var deleted = 0;
        foreach (var directory in Directory.EnumerateDirectories(_paths.ManagedTunnelClientVersionsDirectory))
        {
            var full = Path.GetFullPath(directory);
            if (string.Equals(full, currentDirectory, StringComparison.OrdinalIgnoreCase))
                continue;

            if (TryDeleteDirectory(full))
                deleted++;
        }

        return deleted;
    }

    public void CleanupOldVersions(string currentVersion, int versionsToKeep = 2)
    {
        if (versionsToKeep < 1) versionsToKeep = 1;
        if (!Directory.Exists(_paths.ManagedTunnelClientVersionsDirectory)) return;

        string currentDirectory;
        try
        {
            currentDirectory = Path.GetFullPath(_paths.GetManagedTunnelClientVersionDirectory(currentVersion));
        }
        catch (InvalidDataException)
        {
            return;
        }

        var directories = Directory.EnumerateDirectories(_paths.ManagedTunnelClientVersionsDirectory)
            .Select(static path => new DirectoryInfo(path))
            .OrderByDescending(static info => info.LastWriteTimeUtc)
            .ToArray();

        var fallbackSlots = Math.Max(0, versionsToKeep - 1);
        foreach (var directory in directories)
        {
            var full = Path.GetFullPath(directory.FullName);
            if (string.Equals(full, currentDirectory, StringComparison.OrdinalIgnoreCase))
                continue;

            if (fallbackSlots > 0)
            {
                fallbackSlots--;
                continue;
            }

            TryDeleteDirectory(full);
        }
    }

    public static string GetWindowsAssetArchitecture(Architecture architecture) => architecture switch
    {
        Architecture.X64 => "amd64",
        Architecture.Arm64 => "arm64",
        _ => throw new PlatformNotSupportedException(
            $"当前架构 {architecture} 不受官方 tunnel-client Windows 发布支持")
    };

    public void Dispose()
    {
        if (_ownsHttpClient) _httpClient.Dispose();
    }

    private async Task DownloadAndInstallAsync(
        TunnelClientReleaseInfo release,
        IProgress<ManagedTunnelClientProgress>? progress,
        CancellationToken cancellationToken)
    {
        var operationDirectory = Path.Combine(
            _paths.ManagedTunnelClientUpdateDirectory,
            Guid.NewGuid().ToString("N"));
        var zipPath = Path.Combine(operationDirectory, release.AssetName);
        var extractDirectory = Path.Combine(operationDirectory, "extract");
        var installDirectory = Path.Combine(operationDirectory, "install");
        var targetDirectory = _paths.GetManagedTunnelClientVersionDirectory(release.Version);

        Directory.CreateDirectory(operationDirectory);
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(DownloadTimeout);

            using (var response = await _httpClient.GetAsync(
                       release.DownloadUri,
                       HttpCompletionOption.ResponseHeadersRead,
                       timeoutCts.Token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength;
                await using var source = await response.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
                await using var destination = new FileStream(
                    zipPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                var buffer = new byte[128 * 1024];
                long bytesReceived = 0;
                progress?.Report(new ManagedTunnelClientProgress(
                    ManagedTunnelClientProgressStage.Downloading,
                    $"正在下载 {release.Version}…",
                    0,
                    totalBytes));

                while (true)
                {
                    var read = await source.ReadAsync(buffer, timeoutCts.Token).ConfigureAwait(false);
                    if (read == 0) break;
                    await destination.WriteAsync(buffer.AsMemory(0, read), timeoutCts.Token).ConfigureAwait(false);
                    bytesReceived += read;
                    progress?.Report(new ManagedTunnelClientProgress(
                        ManagedTunnelClientProgressStage.Downloading,
                        $"正在下载 {release.Version}…",
                        bytesReceived,
                        totalBytes));
                }

                await destination.FlushAsync(timeoutCts.Token).ConfigureAwait(false);
            }

            progress?.Report(new ManagedTunnelClientProgress(
                ManagedTunnelClientProgressStage.Verifying,
                "正在验证下载文件…"));
            var actualSha256 = await ComputeSha256Async(zipPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actualSha256, release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"tunnel-client 下载校验失败：期望 {release.Sha256}，实际 {actualSha256}");
            }

            progress?.Report(new ManagedTunnelClientProgress(
                ManagedTunnelClientProgressStage.Installing,
                "正在安装…"));
            ExtractZipSafely(zipPath, extractDirectory);
            var extractedExecutable = Directory
                .EnumerateFiles(extractDirectory, "tunnel-client.exe", SearchOption.AllDirectories)
                .OrderBy(static path => path.Count(ch => ch is '\\' or '/'))
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(extractedExecutable))
            {
                throw new InvalidDataException($"{release.AssetName} 中未找到 tunnel-client.exe");
            }

            var packageDirectory = Path.GetDirectoryName(extractedExecutable)!;
            CopyDirectory(packageDirectory, installDirectory);
            var stagedExecutable = Path.Combine(installDirectory, "tunnel-client.exe");
            _ = await ValidateExecutableAsync(stagedExecutable, cancellationToken).ConfigureAwait(false);

            if (Directory.Exists(targetDirectory))
            {
                TryDeleteDirectory(targetDirectory);
                if (Directory.Exists(targetDirectory))
                {
                    throw new IOException($"无法替换不完整的 tunnel-client 目录：{targetDirectory}");
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetDirectory)!);
            Directory.Move(installDirectory, targetDirectory);
        }
        finally
        {
            TryDeleteDirectory(operationDirectory);
        }
    }

    private async Task<string?> ReadChecksumFromManifestAsync(
        Uri manifestUri,
        string assetName,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            manifestUri,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        foreach (var rawLine in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = rawLine.Trim();
            if (line.Length < 66) continue;

            var hash = line[..64];
            if (!IsSha256(hash)) continue;

            var name = line[64..].TrimStart();
            if (name.StartsWith('*')) name = name[1..];
            if (string.Equals(name.Trim(), assetName, StringComparison.Ordinal))
                return hash.ToLowerInvariant();
        }

        return null;
    }

    private static void ExtractZipSafely(string zipPath, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        var destinationRoot = Path.GetFullPath(destinationDirectory);
        var destinationPrefix = destinationRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.FullName)) continue;

            var destinationPath = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
            if (!destinationPath.StartsWith(destinationPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"ZIP 包含越界路径：{entry.FullName}");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, file);
            var destination = Path.Combine(destinationDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static async Task<string> ValidateExecutableAsync(
        string executablePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(executablePath))
            throw new FileNotFoundException("下载后的 tunnel-client.exe 不存在", executablePath);

        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath)!
        };
        startInfo.ArgumentList.Add("--version");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动下载后的 tunnel-client.exe");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ValidationTimeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKillProcessTree(process);
            throw new TimeoutException("验证 tunnel-client.exe 超时");
        }

        var standardOutput = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var standardError = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidDataException(
                $"tunnel-client.exe --version 失败（exit {process.ExitCode}）：{FirstNonEmpty(standardError, standardOutput)}");
        }

        var versionText = FirstNonEmpty(standardOutput, standardError)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(versionText))
            throw new InvalidDataException("tunnel-client.exe --version 未返回版本信息");

        return versionText;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
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

    private static string? NormalizeSha256Digest(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        const string prefix = "sha256:";
        var normalized = value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? value[prefix.Length..].Trim()
            : value.Trim();
        return IsSha256(normalized) ? normalized.ToLowerInvariant() : null;
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(static ch =>
            ch is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static string ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    private static bool ReadBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
        value.GetBoolean();

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = DownloadTimeout };
        EnsureHeaders(client);
        return client;
    }

    private static void EnsureHeaders(HttpClient client)
    {
        if (client.DefaultRequestHeaders.UserAgent.Count == 0)
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OpenAITunnelManager", "1.0"));
        if (!client.DefaultRequestHeaders.Accept.Any())
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (!client.DefaultRequestHeaders.Contains("X-GitHub-Api-Version"))
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    private static bool TryDeleteDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return false;
            Directory.Delete(path, recursive: true);
            return !Directory.Exists(path);
        }
        catch
        {
            return false;
        }
    }
}
