using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenAITunnelManager.App.ViewModels;
using Windows.Graphics;

namespace OpenAITunnelManager.App;

public sealed partial class MainWindow : Window
{
    public ConnectionsViewModel ViewModel { get; }

    public MainWindow(ConnectionsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        RootGrid.DataContext = ViewModel;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SystemBackdrop = new MicaBackdrop();
        AppWindow.Resize(new SizeInt32(1200, 760));
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshAsync();
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
