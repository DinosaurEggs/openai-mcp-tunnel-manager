using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using OpenAITunnelManager.App.ViewModels;
using OpenAITunnelManager.Core.Abstractions;
using OpenAITunnelManager.Infrastructure.TunnelClient;

namespace OpenAITunnelManager.App;

public partial class App : Application
{
    private Window? _window;

    public static IHost Host { get; } = Microsoft.Extensions.Hosting.Host
        .CreateDefaultBuilder()
        .ConfigureServices(static services =>
        {
            services.AddSingleton(new TunnelClientOptions());
            services.AddSingleton<ITunnelClientService, TunnelClientService>();
            services.AddSingleton<ConnectionsViewModel>();
            services.AddSingleton<MainWindow>();
        })
        .Build();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        await Host.StartAsync();
        _window = Host.Services.GetRequiredService<MainWindow>();
        _window.Activate();
    }
}
