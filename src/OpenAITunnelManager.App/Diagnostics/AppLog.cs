using System.Runtime.InteropServices;
using System.Text;

namespace OpenAITunnelManager.App.Diagnostics;

internal static class AppLog
{
    private static readonly object Sync = new();

    public static string LogFilePath { get; } = BuildLogFilePath();

    public static void Startup()
    {
        Write(
            "INFO",
            $"Process started | OS={Environment.OSVersion} | Arch={RuntimeInformation.ProcessArchitecture} | .NET={Environment.Version} | BaseDir={AppContext.BaseDirectory}");
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

            lock (Sync)
            {
                var directory = Path.GetDirectoryName(LogFilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(LogFilePath, builder.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never turn a recoverable startup failure into another crash.
        }
    }

    private static string BuildLogFilePath() =>
        Path.Combine(AppContext.BaseDirectory, "logs", "app.log");
}
