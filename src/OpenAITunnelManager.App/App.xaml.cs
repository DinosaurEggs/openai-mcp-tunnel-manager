using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using OpenAITunnelManager.App.Diagnostics;
using OpenAITunnelManager.App.ViewModels;
using OpenAITunnelManager.Core.Abstractions;
using OpenAITunnelManager.Infrastructure.TunnelClient;

namespace OpenAITunnelManager.App;

public partial class App : Application
{
    private Window? _window;

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

            _window.Activate();
            AppLog.Info("MainWindow activated");
        }
        catch (Exception exception)
        {
            AppLog.Fatal("Application launch failed", exception);
            throw;
        }
    }

    private static IHost CreateHost()
    {
        try
        {
            var host = Microsoft.Extensions.Hosting.Host
                .CreateDefaultBuilder()
                .ConfigureServices(static services =>
                {
                    services.AddSingleton(new TunnelClientOptions());
                    services.AddSingleton<ITunnelClientService, TunnelClientService>();
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

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        AppLog.Fatal("WinUI unhandled exception", e.Exception);
    }

    private static void OnAppDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            AppLog.Fatal($"AppDomain unhandled exception | terminating={e.IsTerminating}", exception);
        }
        else
        {
            AppLog.Info($"AppDomain unhandled non-Exception object | terminating={e.IsTerminating} | value={e.ExceptionObject}");
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLog.Error("Unobserved task exception", e.Exception);
    }
}
