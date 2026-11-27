using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OpenAITunnelManager.Core.Models;

namespace OpenAITunnelManager.Infrastructure.TunnelClient;

public sealed partial class TunnelClientOperations
{
    private const int MaxHealthResponseBytes = 1024 * 1024;
    private static readonly HttpClient HealthClient = CreateHealthClient();

    public async Task<TunnelConnection> GetStatusAsync(TunnelConnection connection, CancellationToken cancellationToken = default, string? secret = null)
    {
        if (connection.HasRuntime && (!connection.UseForeground || !_foreground.ContainsKey(connection.Identity)))
            return await _inventory.GetStatusAsync(connection, cancellationToken, secret);
        if (!connection.HasProfile || !_foreground.TryGetValue(connection.Identity, out var record)) return connection with { State = RuntimeState.Configured, ProcessRunning = false, Healthy = false, Ready = false, ProcessId = null };
        int processId;
        lock (record)
        {
            if (!_foreground.TryGetValue(connection.Identity, out var current) || !ReferenceEquals(current, record) ||
                !SamePath(record.ProfilePath, connection.ProfilePath))
                return connection with { State = RuntimeState.Configured, ProcessRunning = false, Healthy = false, Ready = false, ProcessId = null };
            if (record.Process.HasExited)
            {
            return connection with
            {
                State = RuntimeState.Error,
                ProcessRunning = false,
                LogPath = record.LogPath,
                Error = $"Profile 前台进程已退出，退出码 {record.Process.ExitCode}"
                };
            }
            processId = record.Process.Id;
        }

        var healthUrl = string.Empty;
        try { if (File.Exists(record.HealthFile)) healthUrl = (await File.ReadAllTextAsync(record.HealthFile, cancellationToken)).Trim(); }
        catch (IOException) { }
        var healthy = !string.IsNullOrWhiteSpace(healthUrl) && await ProbeAsync(healthUrl, "/healthz", cancellationToken);
        var ready = !string.IsNullOrWhiteSpace(healthUrl) && await ProbeAsync(healthUrl, "/readyz", cancellationToken);
        if (!_foreground.TryGetValue(connection.Identity, out var live) || !ReferenceEquals(live, record))
            return connection with { State = RuntimeState.Stopped, ProcessRunning = false, Healthy = false, Ready = false, ProcessId = null, LogPath = record.LogPath };
        var metadata = ProfileMetadataReader.Read(connection.ProfilePath);
        var detailsUrl = string.Empty;
        var mcpUrl = string.Empty;
        var adminUrl = string.Empty;
        if (healthy && TryLoopbackUri(healthUrl, out var localUri))
        {
            var candidate = new Uri(localUri, "/health/mcp").AbsoluteUri;
            var payload = await FetchHealthAsync(candidate, cancellationToken);
            try
            {
                using var snapshot = JsonDocument.Parse(payload);
                var root = snapshot.RootElement;
                if (root.TryGetProperty("schema_version", out var schema) && schema.TryGetInt32(out var version) && version == 1 &&
                    root.TryGetProperty("component", out var component) && component.GetString() == "mcp")
                {
                    mcpUrl = candidate;
                    detailsUrl = new Uri(localUri, "/health?details=true").AbsoluteUri;
                }
            }
            catch (JsonException) { }
            catch (InvalidOperationException) { }
            var basePath = metadata.AdminBasePath.Trim('/');
            adminUrl = new Uri(localUri, "/" + (basePath.Length == 0 ? "" : basePath + "/") + "ui").AbsoluteUri;
        }
        return connection with
        {
            HealthDetailsUrl = detailsUrl,
            McpHealthUrl = mcpUrl,
            AdminUiUrl = adminUrl,
            Residency = metadata.Residency,
            Authentication = metadata.Authentication,
            RefreshedAt = DateTimeOffset.UtcNow,
            State = ready ? RuntimeState.Ready : healthy ? RuntimeState.Running : RuntimeState.Starting,
            ProcessRunning = true,
            Healthy = healthy,
            Ready = ready,
            HealthUrl = healthUrl,
            LogPath = record.LogPath,
            ProcessId = processId,
            Error = string.Empty
        };
    }

    public async Task<string> DoctorAsync(TunnelConnection connection, string? secret, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "doctor" };
        var profilePath = FirstNonEmpty(connection.ProfilePath, connection.RuntimeProfilePath);
        if (connection.ProfileListed && !string.IsNullOrWhiteSpace(connection.ProfileName)) args.AddRange(["--profile", connection.ProfileName]);
        else if (!string.IsNullOrWhiteSpace(profilePath)) args.AddRange(["--profile-file", profilePath]);
        else throw new InvalidOperationException("该项目没有可供 doctor 使用的 tunnel-client Profile");
        args.Add("--explain");
        var metadata = ProfileMetadataReader.Read(profilePath);
        var result = await RunAsync(args, false, cancellationToken, TimeSpan.FromSeconds(60), metadata.ApiKeyRef, secret);
        return result.StandardOutput;
    }

    public async Task<string> ReadLogTailAsync(string path, int maxBytes = 512 * 1024, int maxLines = 5000, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        maxBytes = Math.Clamp(maxBytes, 4096, 4 * 1024 * 1024);
        maxLines = Math.Clamp(maxLines, 1, 20000);
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) throw new FileNotFoundException($"日志文件不存在：{full}", full);
        await using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var start = Math.Max(0, stream.Length - maxBytes);
        stream.Seek(start, SeekOrigin.Begin);
        var buffer = new byte[checked((int)Math.Min(maxBytes, stream.Length - start))];
        var read = await stream.ReadAsync(buffer, cancellationToken);
        var span = buffer.AsSpan(0, read);
        if (start > 0)
        {
            var newline = span.IndexOf((byte)'\n');
            if (newline >= 0) span = span[(newline + 1)..];
        }
        var lines = Encoding.UTF8.GetString(span).Replace("\r\n", "\n").Split('\n');
        if (lines.Length > maxLines) lines = lines[^maxLines..];
        return string.Join(Environment.NewLine, lines).TrimEnd('\r', '\n');
    }

    public async Task<string> GetDetailedHealthAsync(TunnelConnection connection, CancellationToken cancellationToken = default, string? secret = null)
    {
        var current = await GetStatusAsync(connection, cancellationToken, secret);
        var endpoints = new[] { ("Health details", current.HealthDetailsUrl), ("MCP health", current.McpHealthUrl) };
        var output = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(current.ControlPlaneState)) output.AppendLine($"Control Plane: {current.ControlPlaneState} {current.ControlPlaneReason}");
        if (!string.IsNullOrWhiteSpace(current.RemoteLookupStatus)) output.AppendLine($"Remote lookup: {current.RemoteLookupStatus}");
        if (current.RepairActions.Count > 0) output.AppendLine(current.RepairText).AppendLine();
        if (endpoints.All(static endpoint => string.IsNullOrWhiteSpace(endpoint.Item2)))
            return output.AppendLine("未声明详细健康接口").ToString().TrimEnd();
        foreach (var (title, url) in endpoints)
        {
            if (string.IsNullOrWhiteSpace(url)) continue;
            output.AppendLine($"## {title}");
            output.AppendLine(await FetchHealthAsync(url, cancellationToken));
            output.AppendLine();
        }
        return output.ToString().TrimEnd();
    }

    private static HttpClient CreateHealthClient()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
    }

    private static async Task<bool> ProbeAsync(string baseOrUrl, string suffix, CancellationToken cancellationToken)
    {
        var baseUrl = baseOrUrl.Trim();
        foreach (var current in new[] { "/healthz", "/readyz" }) if (baseUrl.EndsWith(current, StringComparison.OrdinalIgnoreCase)) baseUrl = baseUrl[..^current.Length];
        if (!TryLoopbackUri(baseUrl.TrimEnd('/'), out var uri)) return false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(uri, suffix));
            using var response = await HealthClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException) { return false; }
    }

    private static async Task<string> FetchHealthAsync(string url, CancellationToken cancellationToken)
    {
        if (!TryLoopbackUri(url, out var uri)) return $"拒绝访问非 loopback Health URL：{url}";
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(3));
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await HealthClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);

            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var location = response.Headers.Location?.ToString() ?? "(未提供 Location)";
                return $"拒绝跟随 Health 重定向：HTTP {(int)response.StatusCode} -> {location}";
            }

            var text = await ReadBoundedContentAsync(response.Content, deadline.Token);
            try { using var json = JsonDocument.Parse(text); return JsonSerializer.Serialize(json.RootElement, new JsonSerializerOptions { WriteIndented = true }); }
            catch (JsonException) { return $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}{Environment.NewLine}{text}"; }
        }
        catch (InvalidDataException exception)
        {
            return $"拒绝读取 Health 响应：{exception.Message}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return "Health 请求超时"; }
        catch (HttpRequestException exception) { return $"请求失败：{exception.Message}"; }
    }

    private static async Task<string> ReadBoundedContentAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaxHealthResponseBytes)
        {
            throw new InvalidDataException($"响应超过 {MaxHealthResponseBytes / 1024} KiB 限制");
        }

        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (memory.Length + read > MaxHealthResponseBytes)
            {
                throw new InvalidDataException($"响应超过 {MaxHealthResponseBytes / 1024} KiB 限制");
            }
            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return Encoding.UTF8.GetString(memory.GetBuffer(), 0, checked((int)memory.Length));
    }

    private static bool TryLoopbackUri(string value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) &&
            (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps) && string.IsNullOrEmpty(parsed.UserInfo) &&
            (string.Equals(parsed.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
             (IPAddress.TryParse(parsed.DnsSafeHost.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address))))
        {
            uri = parsed; return true;
        }
        uri = null!; return false;
    }
}
