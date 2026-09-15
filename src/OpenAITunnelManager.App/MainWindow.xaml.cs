using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenAITunnelManager.App.Diagnostics;
using OpenAITunnelManager.App.ViewModels;
using Windows.Graphics;

namespace OpenAITunnelManager.App;

public sealed partial class MainWindow : Window
{
    public ConnectionsViewModel ViewModel { get; }

    public MainWindow(ConnectionsViewModel viewModel)
    {
        AppLog.Info("MainWindow construction started");

        ViewModel = viewModel;
        InitializeComponent();
        RootGrid.DataContext = ViewModel;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        try
        {
            SystemBackdrop = new MicaBackdrop();
        }
        catch (Exception exception)
        {
            AppLog.Error("Mica backdrop initialization failed; continuing without Mica", exception);
        }

        try
        {
            AppWindow.Resize(new SizeInt32(1200, 760));
        }
        catch (Exception exception)
        {
            AppLog.Error("Initial window resize failed; continuing with system default size", exception);
        }

        AppLog.Info("MainWindow construction completed");
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        AppLog.Info("MainWindow loaded; refreshing tunnel-client state");
        await ViewModel.RefreshAsync();
        AppLog.Info($"Initial tunnel-client refresh completed: {ViewModel.StatusMessage}");
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ViewModel.StatusMessage = "设置页将在下一阶段接入 settings.json 与 tunnel-client 路径管理";
            return;
        }

        if (args.SelectedItemContainer?.Tag is not string tag || tag == "connections")
        {
            return;
        }

        ViewModel.StatusMessage = tag switch
        {
            "dashboard" => "概览页将在连接管理稳定后接入",
            "logs" => "日志页将在 Runtime log_path 读取服务接入后启用",
            "diagnostics" => "诊断页将在 doctor --explain 服务接入后启用",
            _ => ViewModel.StatusMessage
        };
    }
}
