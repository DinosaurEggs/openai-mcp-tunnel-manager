namespace OpenAITunnelManager.Infrastructure.Settings;

public sealed class AppDataPaths
{
    public static AppDataPaths Current { get; } = new();

    public AppDataPaths()
        : this(AppContext.BaseDirectory)
    {
    }

    public AppDataPaths(string baseDirectory)
    {
        BaseDirectory = Path.GetFullPath(baseDirectory);
        RootDirectory = BaseDirectory;
        ConfigDirectory = Path.Combine(RootDirectory, "config");
        LogsDirectory = Path.Combine(RootDirectory, "logs");
        StateDirectory = Path.Combine(RootDirectory, "state");
        TempDirectory = Path.Combine(StateDirectory, "temp");
        SettingsPath = Path.Combine(ConfigDirectory, "settings.json");
        ManagerLogPath = Path.Combine(LogsDirectory, "app.log");
        ForegroundStateDirectory = Path.Combine(StateDirectory, "foreground");
        ManagedTunnelClientDirectory = Path.Combine(RootDirectory, "tunnel-client");
        ManagedTunnelClientVersionsDirectory = Path.Combine(ManagedTunnelClientDirectory, "versions");
        ManagedTunnelClientUpdateDirectory = Path.Combine(TempDirectory, "tunnel-client-update");

        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(StateDirectory);
        Directory.CreateDirectory(TempDirectory);
        Directory.CreateDirectory(ForegroundStateDirectory);
        Directory.CreateDirectory(ManagedTunnelClientVersionsDirectory);
        Directory.CreateDirectory(ManagedTunnelClientUpdateDirectory);
    }

    public string BaseDirectory { get; }
    public string RootDirectory { get; }
    public string ConfigDirectory { get; }
    public string LogsDirectory { get; }
    public string StateDirectory { get; }
    public string TempDirectory { get; }
    public string ForegroundStateDirectory { get; }
    public string SettingsPath { get; }
    public string ManagerLogPath { get; }
    public string ManagedTunnelClientDirectory { get; }
    public string ManagedTunnelClientVersionsDirectory { get; }
    public string ManagedTunnelClientUpdateDirectory { get; }

    public string GetManagedTunnelClientVersionDirectory(string version)
    {
        var safeVersion = ValidateVersionDirectoryName(version);
        return Path.Combine(ManagedTunnelClientVersionsDirectory, safeVersion);
    }

    public string GetManagedTunnelClientExecutablePath(string version) =>
        Path.Combine(GetManagedTunnelClientVersionDirectory(version), "tunnel-client.exe");

    private static string ValidateVersionDirectoryName(string version)
    {
        var value = version?.Trim() ?? string.Empty;
        if (value.Length == 0 ||
            value is "." or ".." ||
            value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            value.Contains(Path.DirectorySeparatorChar) ||
            value.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidDataException($"无效的 tunnel-client 版本：{version}");
        }

        return value;
    }
}
