namespace OpenAITunnelManager.App.ViewModels;

public partial class ConnectionsViewModel
{
    public async Task ImportProfileAsync(string name, string sourcePath)
    {
        EnsureClientAvailable();
        IsBusy = true;
        StatusMessage = $"正在导入 Profile：{name.Trim()}";
        try
        {
            await _operations.ImportProfileAsync(name, sourcePath);
            await RefreshAfterOperationAsync($"profile:{name.Trim()}", $"Profile 已导入：{name.Trim()}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExportSelectedProfileAsync(string destinationPath)
    {
        var item = SelectedConnection ?? throw new InvalidOperationException("请先选择一个配置");
        if (!item.ProfileListed || !item.HasProfile) throw new InvalidOperationException("当前项目没有可导出的 profiles list Profile");
        EnsureClientAvailable();
        IsBusy = true;
        StatusMessage = $"正在导出 Profile：{item.ProfileName}";
        try
        {
            await _operations.ExportProfileAsync(item.ProfileName, item.ProfilePath, destinationPath);
            StatusMessage = $"Profile 已导出：{destinationPath}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
