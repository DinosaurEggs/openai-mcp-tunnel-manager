using System.Runtime.InteropServices;
using System.Text;
using OpenAITunnelManager.Infrastructure.Settings;

namespace OpenAITunnelManager.App.Diagnostics;

internal static class AppLog
{
    private const long MaxLogBytes = 5L * 1024 * 1024;
    private const int BackupCount = 3;
    private static readonly object Sync = new();

    public static string LogFilePath { get; private set; } = AppDataPaths.Current.ManagerLogPath;

    public static void Configure(string logFilePath)
    {
        if (string.IsNullOrWhiteSpace(logFilePath)) return;
        lock (Sync)
        {
            LogFilePath = Path.GetFullPath(logFilePath);
        }
    }

    public static void Startup()
    {
        Write(
            "INFO",
            $"Process started | OS={Environment.OSVersion} | Arch={RuntimeInformation.ProcessArchitecture} | .NET={Environment.Version} | BaseDir={AppContext.BaseDirectory} | DataRoot={AppDataPaths.Current.RootDirectory}");
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception exception) => Write("ERROR", message, exception);

    public static void Fatal(string message, Exception exception) => Write("FATAL", message, exception);

    private static void Write(string level, string message, Exception? exception = null)
    {
        try
        {
            var builder = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
                .Append(" [")
                .Append(level)
                .Append("] ")
                .AppendLine(message);

            if (exception is not null)
            {
                builder.AppendLine(exception.ToString());
            }

            var text = builder.ToString();
            lock (Sync)
            {
                var directory = Path.GetDirectoryName(LogFilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                RotateIfNeeded(Encoding.UTF8.GetByteCount(text));
                File.AppendAllText(LogFilePath, text, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never turn a recoverable startup failure into another crash.
        }
    }

    private static void RotateIfNeeded(int incomingBytes)
    {
        if (!File.Exists(LogFilePath)) return;
        if (new FileInfo(LogFilePath).Length + incomingBytes <= MaxLogBytes) return;

        for (var index = BackupCount; index >= 1; index--)
        {
            var destination = $"{LogFilePath}.{index}";
            var source = index == 1 ? LogFilePath : $"{LogFilePath}.{index - 1}";
            if (!File.Exists(source)) continue;
            File.Move(source, destination, overwrite: true);
        }
    }
}
