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
    private bool _shutdownInProgress;

    public ConnectionsViewModel ViewModel { get; }

    public MainWindow(ConnectionsViewModel viewModel)
    {
        AppLog.Info("MainWindow construction started");
        ViewModel = viewModel;
        InitializeComponent();
        RootGrid.DataContext = ViewModel;
        ConnectionsList.RightTapped += ConnectionsList_RightTapped;
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
        InitializeAnsiLogViewer();
        AppWindow.Closing += AppWindow_Closing;
        AppLog.Info("MainWindow construction completed");
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;
        // InitializeComponent normally establishes this parent relationship already. Repeat here
        // after Loaded as an idempotent fallback in case WinUI deferred the visual parent.
        InitializeAnsiLogViewer();
        if (Navigation.SettingsItem is NavigationViewItem settingsItem) settingsItem.Content = "设置";
        AppLog.Info("MainWindow loaded; initializing settings and tunnel-client state");

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
        if (!IsLogTabSelected() || !ViewModel.LogAutoRefresh || ViewModel.IsBusy) return;
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

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = args.IsSettingsSelected ? "settings" : args.SelectedItemContainer?.Tag as string ?? "connections";
        SelectPage(tag, updateNavigation: false);
    }

    private void SelectPage(string tag, bool updateNavigation = true)
    {
        DashboardPage.Visibility = tag == "dashboard" ? Visibility.Visible : Visibility.Collapsed;
        ConnectionsPage.Visibility = tag == "connections" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;

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

        try
        {
            if (IsLogTabSelected())
            {
                var horizontalOffset = CaptureLogHorizontalOffset();
                await ViewModel.RefreshLogIncrementalAsync();
                RestoreLogViewport(horizontalOffset, followVertical: true);
            }
            else if (IsDiagnosticsTabSelected())
            {
                await ViewModel.RefreshHealthAsync();
            }
        }
        catch (Exception exception)
        {
            ViewModel.StatusMessage = exception.Message;
        }
    }

    private async void ConnectionTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || ViewModel.SelectedConnection is null) return;
        try
        {
            if (IsLogTabSelected())
            {
                ViewModel.RestoreSelectedLogCache();
                var horizontalOffset = CaptureLogHorizontalOffset();
                await ViewModel.RefreshLogIncrementalAsync();
                RestoreLogViewport(horizontalOffset, followVertical: true);
            }
            else if (IsDiagnosticsTabSelected())
            {
                await ViewModel.RefreshHealthAsync();
            }
        }
        catch (Exception exception)
        {
            ViewModel.StatusMessage = exception.Message;
        }
    }

    private bool IsLogTabSelected() => ConnectionTabs.SelectedIndex == 1;
    private bool IsDiagnosticsTabSelected() => ConnectionTabs.SelectedIndex == 2;

    private void ShowLogs_Click(object sender, RoutedEventArgs e) => ConnectionTabs.SelectedIndex = 1;
    private void ShowDiagnostics_Click(object sender, RoutedEventArgs e) => ConnectionTabs.SelectedIndex = 2;

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

        if (!_allowClose)
        {
            args.Cancel = true;
            if (!_shutdownInProgress) _ = ExitApplicationAsync();
            return;
        }

        _logTimer.Stop();
        _ansiLogViewer?.Dispose();
        _ansiLogViewer = null;
        DisposeTray();
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
        if (_shutdownInProgress) return;
        _shutdownInProgress = true;
        _logTimer.Stop();
        try
        {
            await ViewModel.ShutdownAsync();
        }
        catch (Exception exception)
        {
            AppLog.Error("Application shutdown cleanup failed", exception);
        }
        finally
        {
            _allowClose = true;
            _ansiLogViewer?.Dispose();
            _ansiLogViewer = null;
            DisposeTray();
            Close();
        }
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
