using System.Diagnostics;
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
using OpenAITunnelManager.Core.Models;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage.Pickers;

namespace OpenAITunnelManager.App;

public sealed partial class MainWindow : Window
{
    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int SwMinimize = 6;

    private readonly DispatcherQueueTimer _refreshTimer;
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
            AppWindow.Resize(new SizeInt32(1200, 760));
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath)) AppWindow.SetIcon(iconPath);
        }
        catch (Exception exception)
        {
            AppLog.Error("Initial window configuration failed; continuing with system defaults", exception);
        }

        _refreshTimer = DispatcherQueue.CreateTimer();
        _refreshTimer.Tick += RefreshTimer_Tick;
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
        try
        {
            await ViewModel.InitializeAsync();
            SetupTrayIcon();
            ResetTimers();
            if (!ViewModel.IsClientAvailable) SelectPage("settings");
            MissingClientInfo.IsOpen = !ViewModel.IsClientAvailable;
            AppLog.Info($"Initial tunnel-client refresh completed: {ViewModel.StatusMessage}");
        }
        catch (Exception exception)
        {
            AppLog.Error("Initial UI initialization failed", exception);
            await ShowErrorAsync(exception.Message);
            SelectPage("settings");
        }
    }

    private async void RefreshTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (ViewModel.IsBusy) return;
        try
        {
            await ViewModel.RefreshAsync();
            MissingClientInfo.IsOpen = !ViewModel.IsClientAvailable;
        }
        catch (Exception exception)
        {
            AppLog.Error("Background inventory refresh failed", exception);
            ViewModel.StatusMessage = $"刷新失败：{exception.Message}";
        }
    }

    private async void LogTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (LogsPage.Visibility != Visibility.Visible || !ViewModel.LogAutoRefresh || ViewModel.IsBusy) return;
        try
        {
            await ViewModel.RefreshLogAsync();
            ScrollLogToEndIfNeeded();
        }
        catch (Exception exception)
        {
            AppLog.Error("Background log refresh failed", exception);
            ViewModel.StatusMessage = $"读取日志失败：{exception.Message}";
        }
    }

    private void ResetTimers()
    {
        _refreshTimer.Stop();
        _refreshTimer.Interval = TimeSpan.FromSeconds(Math.Max(2, ViewModel.RefreshIntervalSeconds));
        _refreshTimer.Start();
        _logTimer.Start();
    }

    private async void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = args.IsSettingsSelected ? "settings" : args.SelectedItemContainer?.Tag as string ?? "connections";
        SelectPage(tag, updateNavigation: false);
        try
        {
            if (tag == "logs")
            {
                await ViewModel.RefreshLogAsync();
                ScrollLogToEndIfNeeded();
            }
            if (tag == "diagnostics") await ViewModel.RefreshHealthAsync();
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
        try
        {
            await ViewModel.RefreshSelectedStatusAsync();
            if (LogsPage.Visibility == Visibility.Visible)
            {
                await ViewModel.RefreshLogAsync();
                ScrollLogToEndIfNeeded();
            }
        }
        catch (Exception exception)
        {
            ViewModel.StatusMessage = exception.Message;
        }
    }

    private void StartSelected_Click(object sender, RoutedEventArgs e) => SelectPage("logs");
    private void ShowLogs_Click(object sender, RoutedEventArgs e) => SelectPage("logs");
    private void ShowDiagnostics_Click(object sender, RoutedEventArgs e) => SelectPage("diagnostics");

    private async void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsClientAvailable)
        {
            SelectPage("settings");
            await ShowErrorAsync("请先在设置中选择可用的 tunnel-client.exe。");
            return;
        }

        var name = new TextBox { Header = "Profile 名称", PlaceholderText = "idea" };
        var tunnelId = new TextBox { Header = "Tunnel ID", PlaceholderText = "tunnel_..." };
        var type = new ComboBox { Header = "MCP 类型", SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        type.Items.Add("HTTP URL");
        type.Items.Add("STDIO Command");
        var target = new TextBox { Header = "MCP 地址 / 命令", PlaceholderText = "http://127.0.0.1:64343/stream" };
        var secret = new PasswordBox { Header = "Runtime API Key", PlaceholderText = "可留空，填写后仅保存到 Windows 凭据管理器" };
        var enabled = new CheckBox { Content = "启用此配置", IsChecked = true };
        var autoConnect = new CheckBox { Content = "程序启动后自动连接" };
        var autoReconnect = new CheckBox { Content = "异常停止后自动重连" };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Microsoft.UI.Colors.IndianRed) };
        var panel = new StackPanel { Spacing = 10 };
        foreach (var control in new UIElement[] { name, tunnelId, type, target, secret, enabled, autoConnect, autoReconnect, error }) panel.Children.Add(control);

        var dialog = NewDialog("新建 tunnel-client Profile", panel, "创建");
        ProfileSpec? spec = null;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            spec = new ProfileSpec(name.Text, tunnelId.Text, type.SelectedIndex == 1 ? McpType.Stdio : McpType.Http, target.Text);
            var errors = spec.Validate();
            if (errors.Count == 0) return;
            args.Cancel = true;
            error.Text = string.Join(Environment.NewLine, errors);
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary || spec is null) return;
        try
        {
            await ViewModel.CreateProfileAsync(spec, secret.Password, new ProfilePreference
            {
                Enabled = enabled.IsChecked == true,
                AutoConnect = autoConnect.IsChecked == true,
                AutoReconnect = autoReconnect.IsChecked == true
            });
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception.Message);
        }
    }

    private async void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanEditSelected)
        {
            await ShowErrorAsync("当前项目没有可编辑的 profiles list Profile。");
            return;
        }

        try
        {
            var data = await ViewModel.LoadSelectedProfileAsync();
            var tunnelId = new TextBox { Header = "Tunnel ID", Text = data.TunnelId };
            var target = new TextBox { Header = data.TargetKind == "command" ? "main MCP Command" : "main MCP URL", Text = data.TargetValue };
            var secret = new PasswordBox { Header = "新的 Runtime API Key", PlaceholderText = data.HasSavedSecret ? "已保存；留空表示不修改" : "未保存；留空使用环境变量" };
            var deleteSecret = new CheckBox { Content = "删除已保存的 Runtime API Key", IsEnabled = data.HasSavedSecret };
            var enabled = new CheckBox { Content = "启用此配置", IsChecked = data.Preference.Enabled };
            var autoConnect = new CheckBox { Content = "程序启动后自动连接", IsChecked = data.Preference.AutoConnect };
            var autoReconnect = new CheckBox { Content = "异常停止后自动重连", IsChecked = data.Preference.AutoReconnect };
            var raw = new TextBox
            {
                Header = "高级配置（完整 Profile YAML / JSON）",
                Text = data.Text,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new FontFamily("Cascadia Mono"),
                Height = 300,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Microsoft.UI.Colors.IndianRed) };
            var stack = new StackPanel { Spacing = 10 };
            foreach (var control in new UIElement[] { tunnelId, target, enabled, autoConnect, autoReconnect, secret, deleteSecret, raw, error }) stack.Children.Add(control);
            var scroll = new ScrollViewer { Content = stack, MaxHeight = 650, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var dialog = NewDialog($"编辑 Profile - {data.Name}", scroll, "保存");
            dialog.PrimaryButtonClick += (_, args) =>
            {
                var spec = new ProfileSpec(data.Name, tunnelId.Text, data.TargetKind == "command" ? McpType.Stdio : McpType.Http, target.Text);
                var errors = spec.Validate();
                if (errors.Count == 0 && !string.IsNullOrWhiteSpace(raw.Text)) return;
                args.Cancel = true;
                error.Text = errors.Count > 0 ? string.Join(Environment.NewLine, errors) : "Profile 内容不能为空";
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            await ViewModel.SaveSelectedProfileAsync(
                data,
                raw.Text,
                tunnelId.Text,
                target.Text,
                new ProfilePreference
                {
                    Enabled = enabled.IsChecked == true,
                    AutoConnect = autoConnect.IsChecked == true,
                    AutoReconnect = autoReconnect.IsChecked == true
                },
                secret.Password,
                deleteSecret.IsChecked == true);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception.Message);
        }
    }

    private async void EditPreference_Click(object sender, RoutedEventArgs e)
    {
        var item = ViewModel.SelectedConnection;
        if (item is null) return;

        try
        {
            var preference = ViewModel.GetSelectedPreferenceCopy();
            var hasSecret = ViewModel.HasSavedSecret(item);
            var enabled = new CheckBox { Content = "启用此配置", IsChecked = preference.Enabled };
            var autoConnect = new CheckBox { Content = "程序启动后自动连接", IsChecked = preference.AutoConnect };
            var autoReconnect = new CheckBox { Content = "异常停止后自动重连", IsChecked = preference.AutoReconnect };
            var secret = new PasswordBox { Header = "新的 Runtime API Key", PlaceholderText = hasSecret ? "已保存；留空表示不修改" : "未保存；留空使用环境变量" };
            var deleteSecret = new CheckBox { Content = "删除已保存的 Runtime API Key", IsEnabled = hasSecret };
            var panel = new StackPanel { Spacing = 10 };
            foreach (var control in new UIElement[] { enabled, autoConnect, autoReconnect, secret, deleteSecret }) panel.Children.Add(control);
            var dialog = NewDialog($"本机偏好 / 密钥 - {item.Name}", panel, "保存");
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            await ViewModel.SaveSelectedPreferenceAsync(
                new ProfilePreference
                {
                    Enabled = enabled.IsChecked == true,
                    AutoConnect = autoConnect.IsChecked == true,
                    AutoReconnect = autoReconnect.IsChecked == true
                },
                secret.Password,
                deleteSecret.IsChecked == true);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception.Message);
        }
    }

    private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var item = ViewModel.SelectedConnection;
        if (item is null) return;

        var profileLine = item.ProfileListed ? "\n• 如果没有其他 Runtime 共用该 Profile，则删除官方 Profile 文件" : string.Empty;
        var confirm = NewDialog(
            "删除 tunnel-client 配置",
            new TextBlock
            {
                Text = $"确定删除 {item.Name}？\n\n• 停止并移除对应 Runtime alias（如果有）{profileLine}\n• 删除对应本机偏好和不再使用的 Runtime API Key\n\n不会删除 OpenAI 平台上的远程 Tunnel。",
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 520
            },
            "删除");

        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            await ViewModel.DeleteSelectedAsync();
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception.Message);
        }
    }

    private async void BrowseTunnelClient_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
            picker.FileTypeFilter.Add(".exe");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
            var file = await picker.PickSingleFileAsync();
            if (file is not null) ViewModel.SetTunnelClientPath(file.Path);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync($"选择 tunnel-client 失败：{exception.Message}");
        }
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.SaveSettingsAsync();
            ResetTimers();
            MissingClientInfo.IsOpen = !ViewModel.IsClientAvailable;
            if (ViewModel.IsClientAvailable) SelectPage("connections");
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception.Message);
        }
    }

    private void OpenConfigLocation_Click(object sender, RoutedEventArgs e) => OpenFileLocation(ViewModel.GetSelectedConfigPath());
    private void OpenLogLocation_Click(object sender, RoutedEventArgs e) => OpenFileLocation(ViewModel.CurrentLogPath);

    private static void OpenFileLocation(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Path.GetFullPath(path)}\"") { UseShellExecute = true });
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(ViewModel.VisibleLog ?? string.Empty);
        Clipboard.SetContent(package);
        ViewModel.StatusMessage = "已复制当前可见日志";
    }

    private void LogWrap_Changed(object sender, RoutedEventArgs e)
    {
        LogTextBox.TextWrapping = LogWrapCheckBox.IsChecked == true ? TextWrapping.Wrap : TextWrapping.NoWrap;
        LogTextBox.HorizontalScrollBarVisibility = LogWrapCheckBox.IsChecked == true ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
    }

    private void ScrollLogToEndIfNeeded()
    {
        if (!ViewModel.LogAutoRefresh || LogTextBox.Text is null) return;
        LogTextBox.SelectionStart = LogTextBox.Text.Length;
        LogTextBox.SelectionLength = 0;
    }

    private ContentDialog NewDialog(string title, object content, string primaryText)
    {
        return new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = title,
            Content = content,
            PrimaryButtonText = primaryText,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };
    }

    private async Task ShowErrorAsync(string message)
    {
        ViewModel.StatusMessage = message;
        var dialog = NewDialog("错误", new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 540 }, "确定");
        dialog.CloseButtonText = string.Empty;
        await dialog.ShowAsync();
    }

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

        _refreshTimer.Stop();
        _logTimer.Stop();
        DisposeTray();
        _ = ViewModel.ShutdownAsync();
    }

    public void RestoreFromExternalActivation() => RestoreFromTray();

    private void RestoreFromTray()
    {
        ShowWindow(_hwnd, SwShow);
        SetForegroundWindow(_hwnd);
        ViewModel.StatusMessage = "窗口已恢复";
    }

    private async Task ExitApplicationAsync()
    {
        _allowClose = true;
        _refreshTimer.Stop();
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
