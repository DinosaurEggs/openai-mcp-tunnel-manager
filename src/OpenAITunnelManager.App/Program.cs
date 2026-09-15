using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace OpenAITunnelManager.App;

public static class Program
{
    private const string InstanceKey = "OpenAITunnelManager.Main";
    private static App? _app;
    private static bool _pendingActivation;

    [STAThread]
    public static async Task Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
        var instance = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!instance.IsCurrent)
        {
            await instance.RedirectActivationToAsync(activationArgs);
            return;
        }

        instance.Activated += OnActivated;
        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _app = new App();
            if (_pendingActivation)
            {
                _pendingActivation = false;
                _app.HandleRedirectedActivation();
            }
        });
    }

    private static void OnActivated(object? sender, AppActivationArguments args)
    {
        var app = _app;
        if (app is null)
        {
            _pendingActivation = true;
            return;
        }

        app.HandleRedirectedActivation();
    }
}
