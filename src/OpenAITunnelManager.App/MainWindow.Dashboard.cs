using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using OpenAITunnelManager.App.ViewModels;

namespace OpenAITunnelManager.App;

public sealed partial class MainWindow
{
    private bool _dashboardStateConfigured;
    private DateTimeOffset _dashboardLastUpdatedAt;

    private void ConfigureDashboardState()
    {
        if (_dashboardStateConfigured) return;
        _dashboardStateConfigured = true;

        ViewModel.Connections.CollectionChanged += DashboardConnections_CollectionChanged;
        ViewModel.PropertyChanged += DashboardViewModel_PropertyChanged;
        UpdateDashboardState(updateTimestamp: true);
    }

    private void DashboardConnections_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateDashboardState(updateTimestamp: true);
    }

    private void DashboardViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var updateTimestamp = e.PropertyName is nameof(ConnectionsViewModel.ClientVersion)
            or nameof(ConnectionsViewModel.IsClientAvailable)
            or nameof(ConnectionsViewModel.TunnelClientPath);

        if (e.PropertyName is nameof(ConnectionsViewModel.ClientVersion)
            or nameof(ConnectionsViewModel.IsClientAvailable)
            or nameof(ConnectionsViewModel.TunnelClientPath)
            or nameof(ConnectionsViewModel.StartWithWindows)
            or nameof(ConnectionsViewModel.CloseToTray))
        {
            UpdateDashboardState(updateTimestamp);
        }
    }

    private void UpdateDashboardState(bool updateTimestamp)
    {
        if (updateTimestamp || _dashboardLastUpdatedAt == default)
            _dashboardLastUpdatedAt = DateTimeOffset.Now;

        DashboardRunningCount.Text = ViewModel.Connections.Count(item => item.ProcessRunning).ToString();
        DashboardReadyCount.Text = ViewModel.Connections.Count(item => item.ProcessRunning && item.Ready).ToString();
        DashboardProfileCount.Text = ViewModel.Connections.Count(item => item.ProfileListed).ToString();
        DashboardRuntimeCount.Text = ViewModel.Connections.Count(item => item.HasRuntime).ToString();

        DashboardSystemStatusText.Text = ViewModel.IsClientAvailable ? "系统正常" : "需要处理";
        DashboardClientStatusText.Text = ViewModel.IsClientAvailable ? "可用" : "不可用";
        DashboardClientVersionText.Text = ViewModel.ClientVersion;

        var path = ViewModel.TunnelClientPath?.Trim() ?? string.Empty;
        DashboardClientPathText.Text = string.IsNullOrWhiteSpace(path) ? "未显式配置" : path;
        ToolTipService.SetToolTip(DashboardClientPathText, string.IsNullOrWhiteSpace(path) ? null : path);
        ToolTipService.SetToolTip(DashboardClientVersionText, ViewModel.ClientVersion);

        DashboardStartupText.Text = ViewModel.StartWithWindows ? "已开启" : "已关闭";
        DashboardTrayText.Text = ViewModel.CloseToTray ? "已开启" : "已关闭";
        DashboardLastUpdatedText.Text = $"最后更新 {_dashboardLastUpdatedAt:HH:mm:ss}";
    }
}
