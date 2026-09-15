using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenAITunnelManager.Core.Abstractions;
using OpenAITunnelManager.Core.Models;

namespace OpenAITunnelManager.App.ViewModels;

public partial class ConnectionsViewModel(ITunnelClientService tunnelClient) : ObservableObject
{
    public ObservableCollection<TunnelConnection> Connections { get; } = [];

    [ObservableProperty]
    public partial TunnelConnection? SelectedConnection { get; set; }

    [ObservableProperty]
    public partial string ClientVersion { get; set; } = "未检测";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "正在读取 tunnel-client...";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public bool CanStopSelected => SelectedConnection is { HasRuntime: true, ProcessRunning: true } && !IsBusy;

    partial void OnSelectedConnectionChanged(TunnelConnection? value)
    {
        OnPropertyChanged(nameof(CanStopSelected));
        StopSelectedCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStopSelected));
        RefreshCommand.NotifyCanExecuteChanged();
        StopSelectedCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    public async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "正在同步 tunnel-client 状态...";
        var selectedKey = ConnectionKey(SelectedConnection);

        try
        {
            var versionTask = tunnelClient.GetVersionAsync();
            var connectionsTask = tunnelClient.GetConnectionsAsync();
            await Task.WhenAll(versionTask, connectionsTask);

            ClientVersion = versionTask.Result;
            Connections.Clear();
            foreach (var connection in connectionsTask.Result)
            {
                Connections.Add(connection);
            }

            SelectedConnection = Connections.FirstOrDefault(item => ConnectionKey(item) == selectedKey)
                ?? Connections.FirstOrDefault();
            StatusMessage = Connections.Count == 0
                ? "未发现 Profile 或 Runtime"
                : $"已同步 {Connections.Count} 个连接";
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRefresh() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopSelectedAsync()
    {
        var selected = SelectedConnection;
        if (selected is null || !selected.HasRuntime)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = $"正在停止 {selected.RuntimeAlias}...";
        try
        {
            await tunnelClient.StopRuntimeAsync(selected.RuntimeAlias);
            var refreshed = await tunnelClient.GetStatusAsync(selected);
            var index = Connections.IndexOf(selected);
            if (index >= 0)
            {
                Connections[index] = refreshed;
            }

            SelectedConnection = refreshed;
            StatusMessage = $"{selected.RuntimeAlias} 已停止";
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanStop() => CanStopSelected;

    private static string ConnectionKey(TunnelConnection? connection) => connection switch
    {
        null => string.Empty,
        { HasRuntime: true } => $"runtime:{connection.RuntimeAlias}",
        _ => $"profile:{connection.ProfileName}"
    };
}
