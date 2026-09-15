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
}
