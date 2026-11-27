using System.Diagnostics;
using OpenAITunnelManager.Infrastructure.Updates;

namespace OpenAITunnelManager.Updater;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 3 || !int.TryParse(args[0], out var parentId)) return 1;
        var root = args[1];
        var stage = args[2];
        var resultFile = Path.Combine(root, "state", "manager-update-result.txt");
        var payload = Path.Combine(stage, "payload");
        var backup = Path.Combine(stage, "backup");
        try
        {
            Process? parent;
            try { parent = Process.GetProcessById(parentId); }
            catch (ArgumentException) { parent = null; }
            using (parent)
            {
                if (parent is not null)
                {
                    using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(1));
                    await parent.WaitForExitAsync(deadline.Token);
                }
            }
            ManagerUpdateInstaller.Apply(root, payload, backup);
            TryDelete(backup);
            TryDelete(payload);
            File.WriteAllText(resultFile, "Manager 已更新至最新安装的版本。");
        }
        catch (Exception error)
        {
            File.WriteAllText(resultFile, $"Manager 更新失败：{error.Message}");
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(Path.Combine(root, "OpenAITunnelManager.exe"))
            {
                UseShellExecute = true,
                WorkingDirectory = root
            });
            return process is null ? 1 : 0;
        }
        catch (Exception error)
        {
            File.AppendAllText(resultFile, Environment.NewLine + $"自动重启失败：{error.Message}");
            return 1;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
