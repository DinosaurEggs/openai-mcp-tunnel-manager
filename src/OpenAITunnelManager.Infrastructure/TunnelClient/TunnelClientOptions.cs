namespace OpenAITunnelManager.Infrastructure.TunnelClient;

public sealed class TunnelClientOptions
{
    public string ExecutablePath { get; set; } = string.Empty;

    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public string ResolveExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(ExecutablePath))
        {
            return ExecutablePath.Trim();
        }

        var environmentPath = Environment.GetEnvironmentVariable("TUNNEL_CLIENT_PATH");
        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            return environmentPath.Trim();
        }

        var localExecutable = Path.Combine(AppContext.BaseDirectory, "tunnel-client.exe");
        return File.Exists(localExecutable) ? localExecutable : "tunnel-client.exe";
    }
}
