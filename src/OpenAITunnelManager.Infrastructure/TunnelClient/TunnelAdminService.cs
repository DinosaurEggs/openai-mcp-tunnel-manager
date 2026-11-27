using System.Text.Json;

namespace OpenAITunnelManager.Infrastructure.TunnelClient;

public sealed record AdminProfileInfo(string Name, string BaseUrl, string UrlPath, string KeyReference, bool Active);
public sealed record RemoteTunnelInfo(string Id, string Name, string Description, string Json)
{
    public string DisplayName => $"{Name} · {Id}";
}

public sealed class TunnelAdminService(TunnelClientOptions options)
{
    private readonly TunnelClientProcessRunner _runner = new(options);

    public async Task<IReadOnlyList<AdminProfileInfo>> ListProfilesAsync(CancellationToken token = default)
    {
        using var document = await ExecuteAsync(["admin-profiles", "list", "--json"], token);
        return document.RootElement.GetProperty("profiles").EnumerateArray().Select(item => new AdminProfileInfo(
            Read(item, "name"), Read(item, "control_plane_base_url"), Read(item, "control_plane_url_path"),
            Read(item, "admin_key"), item.TryGetProperty("active", out var active) && active.ValueKind == JsonValueKind.True)).ToArray();
    }

    public async Task SaveProfileAsync(string name, string endpoint, string path, string reference, CancellationToken token = default)
    {
        ValidateReference(reference);
        using var result = await ExecuteAsync(["admin-profiles", "set", name, "--control-plane-base-url", endpoint,
            "--control-plane-url-path", path, "--admin-key", reference, "--activate=false", "--json"], token);
    }

    public async Task SetProfileStateAsync(string name, bool delete, CancellationToken token = default)
    {
        using var result = await ExecuteAsync(["admin-profiles", delete ? "delete" : "activate", name, "--json"], token);
    }

    public async Task<IReadOnlyList<RemoteTunnelInfo>> ListTunnelsAsync(AdminProfileInfo profile, string scope, string id, string? secret, CancellationToken token = default)
    {
        using var result = await ExecuteAdminAsync(profile, ["list", ScopeFlag(scope), id], secret, token);
        var tunnels = result.RootElement.GetProperty("tunnels");
        return tunnels.ValueKind == JsonValueKind.Null ? [] : tunnels.EnumerateArray().Select(ParseTunnel).ToArray();
    }

    public async Task<RemoteTunnelInfo> GetTunnelAsync(AdminProfileInfo profile, string id, string? secret, CancellationToken token = default)
    {
        using var result = await ExecuteAdminAsync(profile, ["get", id], secret, token);
        return ParseTunnel(result.RootElement);
    }

    public async Task<RemoteTunnelInfo> CreateTunnelAsync(AdminProfileInfo profile, string name, string description, string scope, string scopeId, string? secret, CancellationToken token = default)
    {
        if (scope == "Tenant") throw new InvalidOperationException("创建 Tunnel 必须使用 Organization 或 Workspace");
        using var result = await ExecuteAdminAsync(profile, ["create", "--name", name, "--description", description, ScopeFlag(scope), scopeId], secret, token);
        return ParseTunnel(result.RootElement);
    }

    public async Task<RemoteTunnelInfo> UpdateTunnelAsync(AdminProfileInfo profile, string id, string name, string description, string? secret, CancellationToken token = default)
    {
        using var result = await ExecuteAdminAsync(profile, ["update", id, "--name", name, "--description", description], secret, token);
        return ParseTunnel(result.RootElement);
    }

    public async Task DeleteTunnelAsync(AdminProfileInfo profile, string id, string? secret, CancellationToken token = default)
    {
        using var result = await ExecuteAdminAsync(profile, ["delete", id, "--confirm"], secret, token);
    }

    public async Task<string> CleanupAsync(bool apply, CancellationToken token = default)
    {
        var args = new List<string> { "runtimes", "cleanup", "--json" };
        if (apply) args.Add("--apply");
        using var result = await ExecuteAsync(args, token);
        return Format(result.RootElement);
    }

    private async Task<JsonDocument> ExecuteAdminAsync(AdminProfileInfo profile, IReadOnlyList<string> operation, string? secret, CancellationToken token)
    {
        var reference = string.IsNullOrEmpty(secret) ? profile.KeyReference : "env:MANAGER_ADMIN_KEY";
        ValidateReference(reference);
        var args = new List<string> { "admin", "--json", "--admin-key", reference };
        if (!string.IsNullOrWhiteSpace(profile.BaseUrl)) args.AddRange(["--control-plane.base-url", profile.BaseUrl]);
        if (!string.IsNullOrWhiteSpace(profile.UrlPath)) args.AddRange(["--control-plane.url-path", profile.UrlPath]);
        args.Add("tunnels");
        args.AddRange(operation);
        return await ExecuteAsync(args, token, reference, secret);
    }

    private async Task<JsonDocument> ExecuteAsync(IEnumerable<string> args, CancellationToken token, string? reference = null, string? secret = null)
    {
        var result = await _runner.RunAsync(args, false, token, secretRef: reference, secret: secret);
        var document = JsonDocument.Parse(result.StandardOutput);
        if (document.RootElement.TryGetProperty("error", out var error))
        {
            var message = error.ToString();
            document.Dispose();
            throw new InvalidOperationException(message);
        }
        return document;
    }

    private static string ScopeFlag(string scope) => scope switch
    {
        "Organization" => "--organization-id", "Workspace" => "--workspace-id", "Tenant" => "--tenant-id",
        _ => throw new ArgumentException("未知 Scope 类型")
    };

    private static void ValidateReference(string reference)
    {
        if (TunnelClientProcessRunner.SecretEnvironmentName(reference) is null &&
            !reference.StartsWith("file:", StringComparison.Ordinal))
            throw new ArgumentException("Admin Key 必须使用 env: 或 file: 引用");
    }

    private static string Read(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static RemoteTunnelInfo ParseTunnel(JsonElement item)
    {
        var id = Read(item, "id");
        if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("官方返回的 Tunnel 缺少 ID");
        return new RemoteTunnelInfo(id, Read(item, "name"), Read(item, "description"), Format(item));
    }

    private static string Format(JsonElement value) => JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
}
