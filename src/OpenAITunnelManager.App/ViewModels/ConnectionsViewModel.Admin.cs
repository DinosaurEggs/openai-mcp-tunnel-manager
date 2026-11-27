using OpenAITunnelManager.Infrastructure.TunnelClient;

namespace OpenAITunnelManager.App.ViewModels;

public partial class ConnectionsViewModel
{
    public async Task<T> ExecuteAdminAsync<T>(Func<TunnelAdminService, Task<T>> operation)
    {
        EnsureClientAvailable();
        IsBusy = true;
        try { return await operation(new TunnelAdminService(_options)); }
        finally { IsBusy = false; }
    }

    public async Task ExecuteAdminAsync(Func<TunnelAdminService, Task> operation)
    {
        await ExecuteAdminAsync(async service => { await operation(service); return true; });
    }
}
