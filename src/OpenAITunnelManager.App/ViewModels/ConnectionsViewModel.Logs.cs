using CommunityToolkit.Mvvm.Input;

namespace OpenAITunnelManager.App.ViewModels;

public partial class ConnectionsViewModel
{
    public void RestoreSelectedLogCache()
    {
        var item = SelectedConnection;
        CurrentLogPath = item is null
            ? string.Empty
            : !string.IsNullOrWhiteSpace(item.LogPath)
                ? item.LogPath
                : _lastLogPaths.GetValueOrDefault(item.Identity, string.Empty);

        // The virtualized ANSI log viewer owns the bounded line cache. These legacy string
        // properties stay empty so search/filter changes cannot accidentally rebuild MiB-sized strings.
        RawLog = string.Empty;
        VisibleLog = string.Empty;
    }

    [RelayCommand]
    public Task RefreshLogIncrementalAsync(CancellationToken cancellationToken = default)
    {
        var item = SelectedConnection;
        if (item is null)
        {
            RestoreSelectedLogCache();
            return Task.CompletedTask;
        }

        var path = !string.IsNullOrWhiteSpace(item.LogPath)
            ? item.LogPath
            : _lastLogPaths.GetValueOrDefault(item.Identity, string.Empty);
        CurrentLogPath = string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path);
        if (!string.IsNullOrWhiteSpace(CurrentLogPath)) _lastLogPaths[item.Identity] = CurrentLogPath;
        RawLog = string.Empty;
        VisibleLog = string.Empty;
        return Task.CompletedTask;
    }
}
