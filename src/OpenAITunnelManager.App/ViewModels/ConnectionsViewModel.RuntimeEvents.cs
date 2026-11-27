using OpenAITunnelManager.Core.Models;

namespace OpenAITunnelManager.App.ViewModels;

public partial class ConnectionsViewModel
{
    private static readonly int[] RuntimeReconnectDelaysMs = [1000, 2000, 5000, 10000, 30000, 60000];
    private const int StatusConcurrency = 4;

    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly Dictionary<string, CancellationTokenSource> _reconnectCancellations = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Task> _reconnectTasks = [];
    private SynchronizationContext? _uiSynchronizationContext;
    private bool _runtimeEventsStarted;

    private void StartRuntimeEvents()
    {
        if (_runtimeEventsStarted) return;
        _runtimeEventsStarted = true;
        _uiSynchronizationContext = SynchronizationContext.Current;
        _operations.ForegroundProfileExited += Operations_ForegroundProfileExited;
    }

    private async Task StopRuntimeEventsAsync()
    {
        if (_runtimeEventsStarted)
        {
            _operations.ForegroundProfileExited -= Operations_ForegroundProfileExited;
            _runtimeEventsStarted = false;
        }

        if (!_lifetimeCancellation.IsCancellationRequested)
        {
            _lifetimeCancellation.Cancel();
        }

        foreach (var cancellation in _reconnectCancellations.Values.ToArray())
        {
            try { cancellation.Cancel(); } catch { }
        }

        await Task.WhenAll(_reconnectTasks.ToArray());
        _reconnectCancellations.Clear();
        _reconnectScheduled.Clear();
    }

    private void Operations_ForegroundProfileExited(string profileName)
    {
        var context = _uiSynchronizationContext;
        if (context is null) return;
        context.Post(async _ => await HandleForegroundProfileExitedAsync(profileName), null);
    }

    private async Task HandleForegroundProfileExitedAsync(string profileName)
    {
        var item = Connections.FirstOrDefault(connection =>
            connection.RunsInForeground &&
            connection.HasProfile &&
            string.Equals(connection.Identity, profileName, StringComparison.OrdinalIgnoreCase));
        if (item is null || _manualStopped.Contains(item.Identity) || _lifetimeCancellation.IsCancellationRequested) return;
        try
        {
            var current = await _operations.GetStatusAsync(item, _lifetimeCancellation.Token, ReadSavedSecret(item));
            if (current.ProcessRunning || _manualStopped.Contains(item.Identity)) return;
        }
        catch (OperationCanceledException) { return; }
        catch (Exception exception) { StatusMessage = exception.Message; return; }
        if (_lifetimeCancellation.IsCancellationRequested || !Connections.Any(connection => connection.Identity == item.Identity && connection.ProfilePath == item.ProfilePath)) return;
        var stopped = item with
        {
            State = RuntimeState.Stopped,
            ProcessRunning = false,
            Healthy = false,
            Ready = false,
            ProcessId = null
        };
        ReplaceConnection(stopped);
        StatusMessage = $"{item.Name} 已退出";
        NotifyOverviewState();
        EvaluateRuntimeReconnect(stopped);
    }

    private async Task<IReadOnlyList<TunnelConnection>> ReadStatusesAsync(
        IReadOnlyCollection<TunnelConnection> connections,
        CancellationToken cancellationToken)
    {
        using var concurrency = new SemaphoreSlim(StatusConcurrency, StatusConcurrency);
        var tasks = connections.Select(async connection =>
        {
            await concurrency.WaitAsync(cancellationToken);
            try
            {
                return await _operations.GetStatusAsync(connection with { UseForeground = FindPreference(connection)?.UseForeground == true }, cancellationToken, ReadSavedSecret(connection));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return connection with { State = RuntimeState.Unknown, UseForeground = FindPreference(connection)?.UseForeground == true, Error = exception.Message };
            }
            finally
            {
                concurrency.Release();
            }
        }).ToArray();
        return await Task.WhenAll(tasks);
    }

    private void EvaluateRuntimeReconnect(TunnelConnection item)
    {
        var identity = item.Identity;
        var preference = FindPreference(item);
        if (preference is not { Enabled: true, AutoReconnect: true } || _manualStopped.Contains(identity))
        {
            CancelReconnect(identity, resetAttempts: true);
            return;
        }

        if (item.ProcessRunning) { CancelReconnect(identity, resetAttempts: true); return; }
        if (item.State is RuntimeState.Configured or RuntimeState.Starting or RuntimeState.Unknown || _lifetimeCancellation.IsCancellationRequested) return;
        if (_reconnectScheduled.Contains(identity)) return;

        _reconnectScheduled.Add(identity);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        _reconnectCancellations[identity] = cancellation;
        var task = ReconnectLoopAsync(identity, item.Name, cancellation);
        _reconnectTasks.Add(task);
        _ = ObserveReconnectAsync(task);
        NotifyActionState();
    }

    private async Task ObserveReconnectAsync(Task task)
    {
        try { await task; }
        finally { _reconnectTasks.Remove(task); }
    }

    private async Task ReconnectLoopAsync(
        string identity,
        string displayName,
        CancellationTokenSource cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                var current = Connections.FirstOrDefault(item =>
                    string.Equals(item.Identity, identity, StringComparison.OrdinalIgnoreCase));
                if (current is null || current.ProcessRunning || _manualStopped.Contains(identity) || !IsClientAvailable || current.State == RuntimeState.Unknown) return;

                var preference = FindPreference(current);
                if (preference is not { Enabled: true, AutoReconnect: true }) return;

                var attempt = _reconnectAttempts.GetValueOrDefault(identity);
                var delayIndex = Math.Min(attempt, RuntimeReconnectDelaysMs.Length - 1);
                var delay = RuntimeReconnectDelaysMs[delayIndex];
                _reconnectAttempts[identity] = attempt + 1;
                StatusMessage = $"{displayName} 将在 {delay / 1000} 秒后自动重连";

                await Task.Delay(delay, cancellation.Token);

                while (IsBusy || IsTunnelClientUpdating)
                    await Task.Delay(100, cancellation.Token);

                current = Connections.FirstOrDefault(item =>
                    string.Equals(item.Identity, identity, StringComparison.OrdinalIgnoreCase));
                if (current is null || current.ProcessRunning || _manualStopped.Contains(identity)) return;
                preference = FindPreference(current);
                if (preference is not { Enabled: true, AutoReconnect: true }) return;

                IsBusy = true;
                try
                {
                    StatusMessage = $"正在自动重连 {displayName}...";
                    await _operations.StartAsync(current, ReadSavedSecret(current), cancellation.Token);
                    await RefreshAfterOperationAsync(SelectedConnection?.Identity, $"{displayName} 已执行自动重连");
                    var refreshed = Connections.FirstOrDefault(item => string.Equals(item.Identity, identity, StringComparison.OrdinalIgnoreCase));
                    if (refreshed?.ProcessRunning == true)
                    {
                        _reconnectAttempts.Remove(identity);
                        return;
                    }
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    StatusMessage = $"{displayName} 自动重连失败：{exception.Message}";
                }
                finally { IsBusy = false; }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (_reconnectCancellations.TryGetValue(identity, out var currentCancellation) &&
                ReferenceEquals(currentCancellation, cancellation))
            {
                _reconnectCancellations.Remove(identity);
                _reconnectScheduled.Remove(identity);
            }
            cancellation.Dispose();
            NotifyActionState();
        }
    }

    private void CancelReconnect(string identity, bool resetAttempts)
    {
        if (_reconnectCancellations.Remove(identity, out var cancellation))
        {
            try { cancellation.Cancel(); } catch { }
        }
        _reconnectScheduled.Remove(identity);
        if (resetAttempts) _reconnectAttempts.Remove(identity);
        NotifyActionState();
    }
}
