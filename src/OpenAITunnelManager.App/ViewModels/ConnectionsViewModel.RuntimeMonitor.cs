using OpenAITunnelManager.Core.Models;

namespace OpenAITunnelManager.App.ViewModels;

public partial class ConnectionsViewModel
{
    private static readonly int[] RuntimeReconnectDelaysMs = [1000, 2000, 5000, 10000, 30000, 60000];
    private static readonly TimeSpan RuntimeMonitorInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReadyStabilityWindow = TimeSpan.FromSeconds(20);
    private const int StatusConcurrency = 4;

    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly Dictionary<string, CancellationTokenSource> _reconnectCancellations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _readySince = new(StringComparer.OrdinalIgnoreCase);
    private Task? _runtimeMonitorTask;

    private void StartRuntimeMonitor()
    {
        if (_runtimeMonitorTask is not null || !IsClientAvailable) return;
        _runtimeMonitorTask = RuntimeMonitorLoopAsync(_lifetimeCancellation.Token);
    }

    private async Task StopRuntimeMonitorAsync()
    {
        if (!_lifetimeCancellation.IsCancellationRequested)
        {
            _lifetimeCancellation.Cancel();
        }

        foreach (var cancellation in _reconnectCancellations.Values.ToArray())
        {
            try { cancellation.Cancel(); } catch { }
        }

        if (_runtimeMonitorTask is null) return;
        try { await _runtimeMonitorTask; }
        catch (OperationCanceledException) { }
        finally { _runtimeMonitorTask = null; }
    }

    private async Task RuntimeMonitorLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(RuntimeMonitorInterval);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RefreshKnownStatusesAsync(cancellationToken);
                await timer.WaitForNextTickAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                StatusMessage = $"Runtime 状态监控失败：{exception.Message}";
                try { await Task.Delay(RuntimeMonitorInterval, cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            }
        }
    }

    private async Task RefreshKnownStatusesAsync(CancellationToken cancellationToken)
    {
        if (!IsClientAvailable || IsBusy || Connections.Count == 0) return;

        var snapshot = Connections.ToArray();
        var statuses = await ReadStatusesAsync(snapshot, cancellationToken);
        foreach (var status in statuses)
        {
            ReplaceConnection(status);
            if (!string.IsNullOrWhiteSpace(status.LogPath))
            {
                _lastLogPaths[status.Identity] = status.LogPath;
                if (SelectedConnection is not null &&
                    string.Equals(SelectedConnection.Identity, status.Identity, StringComparison.OrdinalIgnoreCase))
                {
                    CurrentLogPath = status.LogPath;
                }
            }
            EvaluateRuntimeReconnect(status);
        }
        NotifyOverviewState();
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
                return await _operations.GetStatusAsync(connection, cancellationToken);
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
            _readySince.Remove(identity);
            return;
        }

        if (item.ProcessRunning && item.Ready)
        {
            if (!_readySince.TryGetValue(identity, out var since))
            {
                _readySince[identity] = DateTimeOffset.UtcNow;
            }
            else if (DateTimeOffset.UtcNow - since >= ReadyStabilityWindow)
            {
                _reconnectAttempts.Remove(identity);
            }
            return;
        }

        _readySince.Remove(identity);
        if (item.ProcessRunning || item.State is RuntimeState.Configured or RuntimeState.Starting or RuntimeState.Unknown) return;
        if (_reconnectScheduled.Contains(identity)) return;

        var attempt = _reconnectAttempts.GetValueOrDefault(identity);
        var delayIndex = Math.Min(attempt, RuntimeReconnectDelaysMs.Length - 1);
        var delay = RuntimeReconnectDelaysMs[delayIndex];
        _reconnectAttempts[identity] = attempt + 1;
        _reconnectScheduled.Add(identity);

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        _reconnectCancellations[identity] = cancellation;
        StatusMessage = $"{item.Name} 将在 {delay / 1000} 秒后自动重连";
        _ = ReconnectAfterDelayAsync(identity, item.Name, delay, cancellation);
    }

    private async Task ReconnectAfterDelayAsync(
        string identity,
        string displayName,
        int delayMs,
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(delayMs, cancellation.Token);
            var current = Connections.FirstOrDefault(item =>
                string.Equals(item.Identity, identity, StringComparison.OrdinalIgnoreCase));
            if (current is null || _manualStopped.Contains(identity)) return;

            var preference = FindPreference(current);
            if (preference is not { Enabled: true, AutoReconnect: true } || current.ProcessRunning) return;

            StatusMessage = $"正在自动重连 {displayName}...";
            await _operations.StartAsync(current, ReadSavedSecret(current), cancellation.Token);
            StatusMessage = $"{displayName} 自动重连命令已完成，等待 Ready";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            StatusMessage = $"{displayName} 自动重连失败：{exception.Message}";
        }
        finally
        {
            if (_reconnectCancellations.TryGetValue(identity, out var currentCancellation) &&
                ReferenceEquals(currentCancellation, cancellation))
            {
                _reconnectCancellations.Remove(identity);
            }
            _reconnectScheduled.Remove(identity);
            cancellation.Dispose();
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
        _readySince.Remove(identity);
    }
}
