using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using OpenAITunnelManager.App.Diagnostics;
using OpenAITunnelManager.App.ViewModels;
using OpenAITunnelManager.Core.Abstractions;
using OpenAITunnelManager.Infrastructure.Settings;
using OpenAITunnelManager.Infrastructure.TunnelClient;
using OpenAITunnelManager.Infrastructure.Windows;
using Windows.Graphics;

namespace OpenAITunnelManager.App;

public partial class App : Application
{
    private Window? _window;
    private bool _pendingRedirectedActivation;

    public static IHost Host { get; } = CreateHost();

    public App()
    {
        AppLog.Startup();
        try
        {
            InitializeComponent();
            UnhandledException += OnUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            AppLog.Info("App.xaml initialized");
        }
        catch (Exception exception)
        {
            AppLog.Fatal("App initialization failed", exception);
            throw;
        }
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppLog.Info("OnLaunched entered");
        try
        {
            await Host.StartAsync();
            AppLog.Info("Generic Host started");
            _window = Host.Services.GetRequiredService<MainWindow>();
            AppLog.Info("MainWindow resolved");

            ApplyDpiAwareInitialWindowPlacement(_window);
            if (_window is MainWindow mainWindow) mainWindow.EnableResponsiveLayout();

            _window.Activate();
            AppLog.Info("MainWindow activated");
            if (_pendingRedirectedActivation)
            {
                _pendingRedirectedActivation = false;
                HandleRedirectedActivation();
            }
        }
        catch (Exception exception)
        {
            AppLog.Fatal("Application launch failed", exception);
            throw;
        }
    }

    internal void HandleRedirectedActivation()
    {
        if (_window is not MainWindow window)
        {
            _pendingRedirectedActivation = true;
            return;
        }

        window.DispatcherQueue.TryEnqueue(window.RestoreFromExternalActivation);
    }

    private static IHost CreateHost()
    {
        try
        {
            var baseDirectory = Path.GetFullPath(AppContext.BaseDirectory);
            InitializeApplicationStorage(baseDirectory);
            var host = Microsoft.Extensions.Hosting.Host
                .CreateDefaultBuilder()
                .UseContentRoot(baseDirectory)
                .ConfigureServices(static services =>
                {
                    services.AddSingleton<TunnelClientOptions>();
                    services.AddSingleton<ISettingsStore, JsonSettingsStore>();
                    services.AddSingleton<ICredentialStore, WindowsCredentialStore>();
                    services.AddSingleton<IAutostartService, WindowsAutostartService>();
                    services.AddSingleton<ITunnelClientService, TunnelClientService>();
                    services.AddSingleton<ITunnelClientOperations, TunnelClientOperations>();
                    services.AddSingleton<ConnectionsViewModel>();
                    services.AddSingleton<MainWindow>();
                })
                .Build();
            AppLog.Info("Generic Host built");
            return host;
        }
        catch (Exception exception)
        {
            AppLog.Fatal("Generic Host creation failed", exception);
            throw;
        }
    }

    private static void InitializeApplicationStorage(string baseDirectory)
    {
        Environment.CurrentDirectory = baseDirectory;
        foreach (var directory in new[] { "config", "logs", "state" })
        {
            Directory.CreateDirectory(Path.Combine(baseDirectory, directory));
        }
        AppLog.Info($"Application storage configured | AppDir={baseDirectory} | Settings={Path.Combine(baseDirectory, "config", "settings.json")} | Log={AppLog.LogFilePath}");
    }

    private static void ApplyDpiAwareInitialWindowPlacement(Window window)
    {
        try
        {
            const double desiredWidthDip = 1200d;
            const double desiredHeightDip = 760d;
            const double minimumWidthDip = 760d;
            const double minimumHeightDip = 560d;
            const double workAreaMarginDip = 24d;

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var dpi = GetDpiForWindow(hwnd);
            var scale = Math.Max(1d, dpi / 96d);
            var displayArea = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;

            var marginPx = (int)Math.Round(workAreaMarginDip * scale);
            var maxWidthPx = Math.Max((int)Math.Round(minimumWidthDip * scale), workArea.Width - (marginPx * 2));
            var maxHeightPx = Math.Max((int)Math.Round(minimumHeightDip * scale), workArea.Height - (marginPx * 2));
            var widthPx = Math.Min((int)Math.Round(desiredWidthDip * scale), maxWidthPx);
            var heightPx = Math.Min((int)Math.Round(desiredHeightDip * scale), maxHeightPx);
            widthPx = Math.Min(widthPx, workArea.Width);
            heightPx = Math.Min(heightPx, workArea.Height);

            var x = workArea.X + Math.Max(0, (workArea.Width - widthPx) / 2);
            var y = workArea.Y + Math.Max(0, (workArea.Height - heightPx) / 2);
            window.AppWindow.MoveAndResize(new RectInt32(x, y, widthPx, heightPx));

            AppLog.Info($"DPI-aware initial window placement | dpi={dpi} | scale={scale:F2} | physical={widthPx}x{heightPx} | effective≈{widthPx / scale:F0}x{heightPx / scale:F0}");
        }
        catch (Exception exception)
        {
            AppLog.Error("DPI-aware initial window placement failed; keeping system/default size", exception);
        }
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e) =>
        AppLog.Fatal("WinUI unhandled exception", e.Exception);

    private static void OnAppDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception) AppLog.Fatal($"AppDomain unhandled exception | terminating={e.IsTerminating}", exception);
        else AppLog.Info($"AppDomain unhandled non-Exception object | terminating={e.IsTerminating} | value={e.ExceptionObject}");
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) =>
        AppLog.Error("Unobserved task exception", e.Exception);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);
}