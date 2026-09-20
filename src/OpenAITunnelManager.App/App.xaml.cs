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
            if (_window is MainWindow mainWindow)
            {
                mainWindow.EnableResponsiveLayout();
                mainWindow.EnableEmptyStates();
            }

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
                    services.AddSingleton(_ => new ManagedTunnelClientService());
                    services.AddSingleton<TunnelClientProcessRunner>();
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
        var paths = AppDataPaths.Current;
        AppLog.Info($"Application storage configured | Root={paths.RootDirectory} | Settings={paths.SettingsPath} | Log={paths.ManagerLogPath}");
    }

    private static void ApplyDpiAwareInitialWindowPlacement(Window window)
    {
        try
        {
            const double workAreaWidthRatio = 0.80d;
            const double workAreaHeightRatio = 0.80d;

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var dpi = GetDpiForWindow(hwnd);
            var scale = Math.Max(1d, dpi / 96d);
            var displayArea = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;
            var outerBounds = displayArea.OuterBounds;

            var widthPx = Math.Max(1, (int)Math.Round(workArea.Width * workAreaWidthRatio));
            var heightPx = Math.Max(1, (int)Math.Round(workArea.Height * workAreaHeightRatio));

            var workAreaScreenX = outerBounds.X + workArea.X;
            var workAreaScreenY = outerBounds.Y + workArea.Y;
            var x = workAreaScreenX + Math.Max(0, (workArea.Width - widthPx) / 2);
            var y = workAreaScreenY + Math.Max(0, (workArea.Height - heightPx) / 2);
            window.AppWindow.MoveAndResize(new RectInt32(x, y, widthPx, heightPx));

            AppLog.Info($"Display-relative initial window placement | dpi={dpi} | scale={scale:F2} | workArea={workArea.Width}x{workArea.Height} | ratio={workAreaWidthRatio:P0}x{workAreaHeightRatio:P0} | physical={widthPx}x{heightPx} | effective≈{widthPx / scale:F0}x{heightPx / scale:F0} | displayOrigin={outerBounds.X},{outerBounds.Y}");
        }
        catch (Exception exception)
        {
            AppLog.Error("Display-relative initial window placement failed; keeping system/default size", exception);
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
