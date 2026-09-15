namespace OpenAITunnelManager.Infrastructure.Settings;

public sealed class AppDataPaths
{
    public AppDataPaths()
        : this(AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
    {
    }

    internal AppDataPaths(string baseDirectory, string localApplicationData)
    {
        BaseDirectory = Path.GetFullPath(baseDirectory);
        PortableFlagPath = Path.Combine(BaseDirectory, "portable.flag");
        PortableRequested = File.Exists(PortableFlagPath);

        var usePortable = PortableRequested && IsDirectoryWritable(BaseDirectory);
        if (PortableRequested && !usePortable)
        {
            PortableFallbackReason = "检测到 portable.flag，但程序目录不可写，已回退到 LocalAppData";
        }

        var localRoot = string.IsNullOrWhiteSpace(localApplicationData)
            ? Path.Combine(BaseDirectory, ".local")
            : Path.Combine(localApplicationData, "OpenAITunnelManager");

        RootDirectory = usePortable ? BaseDirectory : Path.GetFullPath(localRoot);
        IsPortable = usePortable;
        ConfigDirectory = Path.Combine(RootDirectory, "config");
        LogsDirectory = Path.Combine(RootDirectory, "logs");
        StateDirectory = Path.Combine(RootDirectory, "state");
        SettingsPath = Path.Combine(ConfigDirectory, "settings.json");
        ManagerLogPath = Path.Combine(LogsDirectory, "app.log");
        ForegroundStateDirectory = Path.Combine(StateDirectory, "foreground");

        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(StateDirectory);
        Directory.CreateDirectory(ForegroundStateDirectory);
    }

    public string BaseDirectory { get; }
    public string RootDirectory { get; }
    public string ConfigDirectory { get; }
    public string LogsDirectory { get; }
    public string StateDirectory { get; }
    public string ForegroundStateDirectory { get; }
    public string SettingsPath { get; }
    public string ManagerLogPath { get; }
    public string PortableFlagPath { get; }
    public bool PortableRequested { get; }
    public bool IsPortable { get; }
    public string PortableFallbackReason { get; } = string.Empty;

    private static bool IsDirectoryWritable(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, $".openai-mcp-tunnel-manager-write-{Guid.NewGuid():N}.tmp");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose))
            {
            }
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }
}
