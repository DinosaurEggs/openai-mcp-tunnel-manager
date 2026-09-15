using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using OpenAITunnelManager.App.Diagnostics;
using OpenAITunnelManager.App.ViewModels;

namespace OpenAITunnelManager.App;

public sealed partial class MainWindow : Window
{
    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int SwMinimize = 6;

    private readonly DispatcherQueueTimer _logTimer;
    private readonly nint _hwnd;
    private TaskbarIcon? _trayIcon;
    private bool _initialized;
    private bool _allowClose;

    public ConnectionsViewModel ViewModel { get; }

    public MainWindow(ConnectionsViewModel viewModel)
    {
        AppLog.Info("MainWindow construction started");
        ViewModel = viewModel;
        InitializeComponent();
        RootGrid.DataContext = ViewModel;
        ConnectionsList.RightTapped += ConnectionsList_RightTapped;
        ConfigureProfileTransferFlyout();
        ConfigureUiPolish();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

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
            // Window size/position is applied once by App using a percentage of the current
            // display work area before activation. Do not apply a fixed physical-pixel size
            // here, otherwise WinUI can perform its first Measure pass against stale metrics.
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath)) AppWindow.SetIcon(iconPath);
        }
        catch (Exception exception)
        {
            AppLog.Error("Initial window configuration failed; continuing with system defaults", exception);
        }

        _logTimer = DispatcherQueue.CreateTimer();
        _logTimer.Interval = TimeSpan.FromSeconds(1);
        _logTimer.Tick += LogTimer_Tick;
        AppWindow.Closing += AppWindow_Closing;
        AppLog.Info("MainWindow construction completed");
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;
        if (Navigation.SettingsItem is NavigationViewItem settingsItem) settingsItem.Content = "设置";
        AppLog.Info("MainWindow loaded; initializing settings and tunnel-client state");

        // Tray availability is independent of tunnel-client health/configuration.
        SetupTrayIcon();

        try
        {
            await ViewModel.InitializeForManualRefreshAsync();
            ViewModel.RestoreSelectedLogCache();
            _logTimer.Start();
            ApplyUiPolish();
            if (!ViewModel.IsClientAvailable) SelectPage("settings");
            MissingClientInfo.IsOpen = !ViewModel.IsClientAvailable;
            RequestResponsiveLayout();
            AppLog.Info($"Initial tunnel-client load completed: {ViewModel.StatusMessage}");
        }
        catch (Exception exception)
        {
            AppLog.Error("Initial UI initialization failed", exception);
            await ShowErrorAsync(exception.Message);
            SelectPage("settings");
        }
    }

    private async void LogTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (LogsPage.Visibility != Visibility.Visible || !ViewModel.LogAutoRefresh || ViewModel.IsBusy) return;
        var horizontalOffset = CaptureLogHorizontalOffset();
        try
        {
            await ViewModel.RefreshLogIncrementalAsync();
            RestoreLogViewport(horizontalOffset, followVertical: true);
        }
        catch (Exception exception)
        {
            AppLog.Error("Background log refresh failed", exception);
            ViewModel.StatusMessage = $"读取日志失败：{exception.Message}";
        }
    }

    private async void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = args.IsSettingsSelected ? "settings" : args.SelectedItemContainer?.Tag as string ?? "connections";
        SelectPage(tag, updateNavigation: false);
        try
        {
            if (tag == "logs")
            {
                ViewModel.RestoreSelectedLogCache();
                var horizontalOffset = CaptureLogHorizontalOffset();
                await ViewModel.RefreshLogIncrementalAsync();
                RestoreLogViewport(horizontalOffset, followVertical: true);
            }
            else if (tag == "diagnostics")
            {
                await ViewModel.RefreshHealthAsync();
            }
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception.Message);
        }
    }

    private void SelectPage(string tag, bool updateNavigation = true)
    {
        DashboardPage.Visibility = tag == "dashboard" ? Visibility.Visible : Visibility.Collapsed;
        ConnectionsPage.Visibility = tag == "connections" ? Visibility.Visible : Visibility.Collapsed;
        LogsPage.Visibility = tag == "logs" ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsPage.Visibility = tag == "diagnostics" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;

        // Visibility changes can realize a previously-collapsed ScrollViewer only after this
        // event returns. Always request a post-navigation responsive pass instead of relying
        // on the user to trigger SizeChanged by resizing the window.
        RequestResponsiveLayout();
        RequestUiPolish();

        if (!updateNavigation) return;

        if (tag == "settings")
        {
            Navigation.SelectedItem = Navigation.SettingsItem;
            return;
        }

        foreach (var item in Navigation.MenuItems.OfType<NavigationViewItem>())
        {
            if (!string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase)) continue;
            Navigation.SelectedItem = item;
            return;
        }
    }

    private async void ConnectionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || ViewModel.SelectedConnection is null) return;
        ViewModel.RestoreSelectedLogCache();

        // Selecting an item must not query tunnel-client. Inventory/status refresh is now
        // explicit: once during application entry (when a client path is configured), after
        // explicit operations, or when the user presses Refresh.
        if (LogsPage.Visibility != Visibility.Visible) return;

        try
        {
            var horizontalOffset = CaptureLogHorizontalOffset();
            await ViewModel.RefreshLogIncrementalAsync();
            RestoreLogViewport(horizontalOffset, followVertical: true);
        }
        catch (Exception exception)
        {
            ViewModel.StatusMessage = exception.Message;
        }
    }

    private void StartSelected_Click(object sender, RoutedEventArgs e) => SelectPage("logs");
    private void ShowLogs_Click(object sender, RoutedEventArgs e) => SelectPage("logs");
    private void ShowDiagnostics_Click(object sender, RoutedEventArgs e) => SelectPage("diagnostics");

    private void SetupTrayIcon()
    {
        if (_trayIcon is not null) return;

        try
        {
            var openCommand = new RelayCommand(() => DispatcherQueue.TryEnqueue(RestoreFromTray));
            var exitCommand = new RelayCommand(() => DispatcherQueue.TryEnqueue(() => _ = ExitApplicationAsync()));
            var menu = new MenuFlyout();
            menu.Items.Add(new MenuFlyoutItem { Text = "打开", Command = openCommand });
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(new MenuFlyoutItem { Text = "退出", Command = exitCommand });

            _trayIcon = new TaskbarIcon
            {
                ToolTipText = "OpenAI MCP Tunnel Manager",
                IconSource = new BitmapImage(new Uri("ms-appx:///Assets/AppIcon.ico")),
                ContextFlyout = menu,
                LeftClickCommand = openCommand,
                NoLeftClickDelay = true,
                Visibility = Visibility.Visible
            };
            _trayIcon.ForceCreate(enablesEfficiencyMode: false);
            AppLog.Info("WinUI system tray icon created");
        }
        catch (Exception exception)
        {
            AppLog.Error("System tray initialization failed", exception);
            DisposeTray();
        }
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_allowClose && ViewModel.CloseToTray)
        {
            args.Cancel = true;
            if (_trayIcon?.IsCreated == true)
            {
                ShowWindow(_hwnd, SwHide);
                ViewModel.StatusMessage = "已最小化到系统托盘";
            }
            else
            {
                ShowWindow(_hwnd, SwMinimize);
                ViewModel.StatusMessage = "系统托盘不可用，已最小化到任务栏";
            }
            return;
        }

        _logTimer.Stop();
        DisposeTray();
        _ = ViewModel.ShutdownAsync();
    }

    public void RestoreFromExternalActivation() => RestoreFromTray();

    private void RestoreFromTray()
    {
        ShowWindow(_hwnd, SwShow);
        SetForegroundWindow(_hwnd);
        RequestResponsiveLayout();
        RequestUiPolish();
        ViewModel.StatusMessage = "窗口已恢复";
    }

    private async Task ExitApplicationAsync()
    {
        _allowClose = true;
        _logTimer.Stop();
        await ViewModel.ShutdownAsync();
        DisposeTray();
        Close();
    }

    private void DisposeTray()
    {
        if (_trayIcon is null) return;
        try
        {
            _trayIcon.Dispose();
        }
        catch (Exception exception)
        {
            AppLog.Error("System tray disposal failed", exception);
        }
        finally
        {
            _trayIcon = null;
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hWnd);
}
