using OpenAITunnelManager.Core.Models;

namespace OpenAITunnelManager.App.ViewModels;

public partial class ConnectionsViewModel
{
    public async Task<IReadOnlyList<string>> RunBatchAsync(string action, IReadOnlyList<string> identities)
    {
        EnsureClientAvailable();
        if (action is not ("启动" or "停止" or "重启")) throw new ArgumentException("未知批量操作");
        var results = new List<string>();
        var selected = SelectedConnection?.Identity;
        IsBusy = true;
        try
        {
            foreach (var identity in identities.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var item = Connections.FirstOrDefault(value => value.Identity == identity);
                if (item is null) { results.Add($"{identity}：已不存在"); continue; }
                try
                {
                    CancelReconnect(identity, resetAttempts: true);
                    if (action == "停止")
                    {
                        _manualStopped.Add(identity);
                        if (item.ProcessRunning) await _operations.StopAsync(item);
                    }
                    else
                    {
                        if (FindPreference(item) is { Enabled: false }) { results.Add($"{item.Name}：已禁用，跳过"); continue; }
                        _manualStopped.Remove(identity);
                        if (action == "重启" && item.ProcessRunning) await _operations.RestartAsync(item, ReadSavedSecret(item));
                        else if (!item.ProcessRunning) await _operations.StartAsync(item, ReadSavedSecret(item));
                    }
                    results.Add($"{item.Name}：命令完成");
                }
                catch (Exception exception) { results.Add($"{item.Name}：失败，{exception.Message}"); }
            }
            await RefreshAfterOperationAsync(selected, "批量操作结束");
            return results;
        }
        finally { IsBusy = false; }
    }

    public async Task CreateNativeRuntimeAsync(string alias)
    {
        EnsureClientAvailable();
        var profile = SelectedConnection ?? throw new InvalidOperationException("请选择 Profile");
        if (profile.HasRuntime || !profile.HasProfile || profile.ProcessRunning)
            throw new InvalidOperationException("请选择已停止且未绑定 Runtime 的 Profile");
        alias = alias.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(alias, "^[a-z0-9][a-z0-9-]*[a-z0-9]$|^[a-z0-9]$"))
            throw new ArgumentException("Runtime alias 只能包含小写字母、数字和内部连字符");
        IsBusy = true;
        try
        {
            var inventory = await _inventory.GetConnectionsAsync();
            if (inventory.Any(item => item.HasRuntime && item.RuntimeAlias == alias))
                throw new InvalidOperationException("该 Runtime alias 已存在");
            CancelReconnect(profile.Identity, resetAttempts: true);
            var runtime = profile with
            {
                Name = alias, RuntimeAlias = alias, RuntimeProfileName = profile.ProfileName,
                RuntimeProfilePath = profile.ProfilePath, UseForeground = false
            };
            await _operations.StartAsync(runtime, ReadSavedSecret(profile));
            await RefreshAfterOperationAsync(runtime.Identity, $"已创建 Runtime：{alias}");
        }
        finally { IsBusy = false; }
    }
}
