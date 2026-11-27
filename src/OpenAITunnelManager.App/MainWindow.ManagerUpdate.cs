using System.Diagnostics;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenAITunnelManager.Infrastructure.Settings;
using OpenAITunnelManager.Infrastructure.Updates;

namespace OpenAITunnelManager.App;

public sealed partial class MainWindow
{
    private CancellationTokenSource? _managerUpdateCancellation;
    private static Version ManagerVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    private async void CheckManagerUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_managerUpdateCancellation is not null || _shutdownInProgress) return;
        using var cancellation = new CancellationTokenSource();
        _managerUpdateCancellation = cancellation;
        CheckManagerUpdateButton.IsEnabled = false;
        ManagerUpdateStatusText.Text = "正在检查更新…";
        string? stage = null;
        var installerStarted = false;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            var service = new ManagerUpdateService(client);
            var release = await service.CheckAsync(ManagerVersion, cancellation.Token);
            if (release is null)
            {
                ManagerUpdateStatusText.Text = "当前已是最新版本";
                var latest = NewDialog("检查更新", new TextBlock { Text = "当前已是最新版本", TextWrapping = TextWrapping.Wrap }, "确定");
                latest.CloseButtonText = string.Empty;
                await latest.ShowAsync();
                return;
            }

            ManagerUpdateStatusText.Text = $"发现新版本 v{release.Version}";
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock
            {
                Text = $"当前版本：v{ManagerVersion.ToString(3)}\n最新版本：v{release.Version}",
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(release.Notes) ? "此版本没有附带更新说明。" : release.Notes,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true
            });
            content.Children.Add(new TextBlock
            {
                Text = "确认更新后下载并自动重启 Manager。Manager 持有的前台连接会随应用退出而停止。",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7
            });
            var dialog = NewDialog("发现 Manager 新版本", content, "确认更新");
            dialog.DefaultButton = ContentDialogButton.Close;
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            var root = AppDataPaths.Current.RootDirectory;
            var helper = Path.Combine(root, "updater", "OpenAITunnelManager.Updater.exe");
            if (!File.Exists(helper))
                throw new FileNotFoundException("当前安装缺少 Manager 更新程序，请使用完整发布包。", helper);
            var updateDirectory = Path.Combine(AppDataPaths.Current.TempDirectory, "manager-update");
            stage = Path.Combine(updateDirectory, Guid.NewGuid().ToString("N"));
            ManagerUpdateStatusText.Text = $"正在下载 v{release.Version}…";
            var progress = new Progress<int?>(value =>
            {
                ManagerUpdateStatusText.Text = value.HasValue
                    ? $"正在下载 v{release.Version}：{value}%"
                    : $"正在下载 v{release.Version}…";
            });
            await service.PrepareAsync(release, stage, progress, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();

            var executable = Path.Combine(updateDirectory, "OpenAITunnelManager.Updater.exe");
            File.Copy(helper, executable, true);
            ManagerUpdateStatusText.Text = "更新包已准备完成，正在重启…";
            var preparedStage = stage;
            await ExitApplicationAsync(() =>
            {
                var info = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = root };
                info.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                info.ArgumentList.Add(root);
                info.ArgumentList.Add(preparedStage);
                using var process = Process.Start(info) ?? throw new InvalidOperationException("无法启动 Manager 更新程序");
                installerStarted = true;
            });
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!_shutdownInProgress) ManagerUpdateStatusText.Text = "已取消更新";
        }
        catch (Exception exception)
        {
            if (!_shutdownInProgress)
            {
                ManagerUpdateStatusText.Text = "检查或下载更新失败";
                await ShowErrorAsync(exception.Message);
            }
        }
        finally
        {
            if (!installerStarted && stage is not null)
            {
                try { if (Directory.Exists(stage)) Directory.Delete(stage, true); } catch { }
            }
            _managerUpdateCancellation = null;
            if (!_shutdownInProgress) CheckManagerUpdateButton.IsEnabled = true;
        }
    }

    private async Task ShowManagerUpdateResultAsync()
    {
        var path = Path.Combine(AppDataPaths.Current.StateDirectory, "manager-update-result.txt");
        if (!File.Exists(path)) return;
        var message = await File.ReadAllTextAsync(path);
        File.Delete(path);
        ManagerUpdateStatusText.Text = message;
        var dialog = NewDialog("Manager 更新结果", new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, "确定");
        dialog.CloseButtonText = string.Empty;
        await dialog.ShowAsync();
    }
}
