using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenAITunnelManager.App.Controls;
using OpenAITunnelManager.App.ViewModels;

namespace OpenAITunnelManager.App;

public sealed partial class MainWindow
{
    private bool _emptyStatesEnabled;
    private bool _initialReadinessNavigationApplied;
    private EmptyStateControl? _dashboardEmptyState;
    private EmptyStateControl? _connectionsEmptyState;
    private EmptyStateControl? _logsEmptyState;
    private EmptyStateControl? _diagnosticsEmptyState;

    internal void EnableEmptyStates()
    {
        if (_emptyStatesEnabled) return;
        _emptyStatesEnabled = true;

        _dashboardEmptyState = AttachEmptyState(DashboardPage);
        _connectionsEmptyState = AttachEmptyState(ConnectionsPage);
        _logsEmptyState = AttachEmptyState(LogsPage);
        _diagnosticsEmptyState = AttachEmptyState(DiagnosticsPage);

        ViewModel.PropertyChanged += ViewModel_PropertyChangedForEmptyStates;
        ViewModel.Connections.CollectionChanged += Connections_CollectionChangedForEmptyStates;
        UpdateEmptyStates();
    }

    private static EmptyStateControl AttachEmptyState(Grid page)
    {
        var emptyState = new EmptyStateControl { Visibility = Visibility.Collapsed };
        Grid.SetRow(emptyState, 0);
        Grid.SetColumn(emptyState, 0);
        Grid.SetRowSpan(emptyState, Math.Max(1, page.RowDefinitions.Count));
        Grid.SetColumnSpan(emptyState, Math.Max(1, page.ColumnDefinitions.Count));
        page.Children.Add(emptyState);
        return emptyState;
    }

    private void ViewModel_PropertyChangedForEmptyStates(object? sender, PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(UpdateEmptyStates);
    }

    private void Connections_CollectionChangedForEmptyStates(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(UpdateEmptyStates);
    }

    private void UpdateEmptyStates()
    {
        if (!_emptyStatesEnabled || _dashboardEmptyState is null || _connectionsEmptyState is null ||
            _logsEmptyState is null || _diagnosticsEmptyState is null)
        {
            return;
        }

        var readiness = ViewModel.ReadinessState;
        switch (readiness)
        {
            case ApplicationReadinessState.TunnelClientNotConfigured:
                ConfigureClientMissingStates(invalidPath: false);
                ShowEmptyState(DashboardPage, _dashboardEmptyState, true);
                ShowEmptyState(ConnectionsPage, _connectionsEmptyState, true);
                ShowEmptyState(LogsPage, _logsEmptyState, true);
                ShowEmptyState(DiagnosticsPage, _diagnosticsEmptyState, true);
                break;

            case ApplicationReadinessState.TunnelClientInvalid:
                ConfigureClientMissingStates(invalidPath: true);
                ShowEmptyState(DashboardPage, _dashboardEmptyState, true);
                ShowEmptyState(ConnectionsPage, _connectionsEmptyState, true);
                ShowEmptyState(LogsPage, _logsEmptyState, true);
                ShowEmptyState(DiagnosticsPage, _diagnosticsEmptyState, true);
                break;

            case ApplicationReadinessState.NoConnections:
                ConfigureNoConnectionsStates();
                ShowEmptyState(DashboardPage, _dashboardEmptyState, true);
                ShowEmptyState(ConnectionsPage, _connectionsEmptyState, true);
                ShowEmptyState(LogsPage, _logsEmptyState, true);
                ShowEmptyState(DiagnosticsPage, _diagnosticsEmptyState, true);
                break;

            default:
                ShowEmptyState(DashboardPage, _dashboardEmptyState, false);
                ShowEmptyState(ConnectionsPage, _connectionsEmptyState, false);
                ConfigureLogState();
                ShowEmptyState(DiagnosticsPage, _diagnosticsEmptyState, ViewModel.SelectedConnection is null);
                if (ViewModel.SelectedConnection is null)
                {
                    _diagnosticsEmptyState.Configure(
                        "\uE9D9",
                        "请选择一个连接",
                        "选择连接后可以运行 doctor，并查看 Runtime 与 MCP Health。",
                        "打开连接页面");
                    _diagnosticsEmptyState.PrimaryAction = NavigateToConnectionsAsync;
                    _diagnosticsEmptyState.SecondaryAction = null;
                }
                break;
        }

        MissingClientInfo.IsOpen = readiness is ApplicationReadinessState.TunnelClientNotConfigured or ApplicationReadinessState.TunnelClientInvalid;
        ApplyInitialReadinessNavigation(readiness);
    }

    private void ConfigureClientMissingStates(bool invalidPath)
    {
        var title = invalidPath ? "tunnel-client 配置无效" : "尚未配置 tunnel-client";
        var description = invalidPath
            ? "当前 tunnel-client 路径不可用。重新选择 tunnel-client.exe 后，Manager 会自动读取已有 Profile 和 Runtime。"
            : "Manager 需要通过 tunnel-client 读取和管理 Profile、Runtime、日志与诊断信息。先完成基础配置即可继续。";

        ConfigureClientRequiredState(_dashboardEmptyState!, "\uE713", title, description);
        ConfigureClientRequiredState(_connectionsEmptyState!, "\uE71B", title, description);
        ConfigureClientRequiredState(_logsEmptyState!, "\uE8A5", "还不能查看日志", description);
        ConfigureClientRequiredState(_diagnosticsEmptyState!, "\uE9D9", "还不能运行诊断", description);
    }

    private void ConfigureClientRequiredState(EmptyStateControl state, string glyph, string title, string description)
    {
        state.Configure(glyph, title, description, "选择 tunnel-client", "打开设置");
        state.PrimaryAction = PickTunnelClientFromEmptyStateAsync;
        state.SecondaryAction = NavigateToSettingsAsync;
    }

    private void ConfigureNoConnectionsStates()
    {
        _dashboardEmptyState!.Configure(
            "\uE71B",
            "还没有连接",
            "tunnel-client 已可用，但尚未发现 Profile 或 Runtime。创建第一个 Profile 后即可启动连接、查看日志和运行诊断。",
            "新建 Profile",
            "刷新");
        _dashboardEmptyState.PrimaryAction = CreateProfileFromEmptyStateAsync;
        _dashboardEmptyState.SecondaryAction = RefreshFromEmptyStateAsync;

        _connectionsEmptyState!.Configure(
            "\uE710",
            "尚未发现 Profile",
            "创建一个新的 tunnel-client Profile，或者在命令行创建后返回这里刷新。",
            "新建 Profile",
            "刷新");
        _connectionsEmptyState.PrimaryAction = CreateProfileFromEmptyStateAsync;
        _connectionsEmptyState.SecondaryAction = RefreshFromEmptyStateAsync;

        _logsEmptyState!.Configure(
            "\uE8A5",
            "还没有连接",
            "创建 Profile 并启动后，运行日志会自动显示在这里。",
            "新建 Profile",
            "打开连接页面");
        _logsEmptyState.PrimaryAction = CreateProfileFromEmptyStateAsync;
        _logsEmptyState.SecondaryAction = NavigateToConnectionsAsync;

        _diagnosticsEmptyState!.Configure(
            "\uE9D9",
            "没有可诊断的连接",
            "创建 Profile 后即可运行 doctor；Runtime 启动后还可以继续检查 Health 与 Ready 状态。",
            "新建 Profile",
            "打开连接页面");
        _diagnosticsEmptyState.PrimaryAction = CreateProfileFromEmptyStateAsync;
        _diagnosticsEmptyState.SecondaryAction = NavigateToConnectionsAsync;
    }

    private void ConfigureLogState()
    {
        if (_logsEmptyState is null) return;
        if (ViewModel.HasCurrentLog)
        {
            ShowEmptyState(LogsPage, _logsEmptyState, false);
            return;
        }

        var item = ViewModel.SelectedConnection;
        if (item is null)
        {
            _logsEmptyState.Configure("\uE8A5", "请选择一个连接", "选择连接后可以查看对应 Runtime 或前台 Profile 的日志。", "打开连接页面");
            _logsEmptyState.PrimaryAction = NavigateToConnectionsAsync;
            _logsEmptyState.SecondaryAction = null;
        }
        else if (item.ProcessRunning)
        {
            _logsEmptyState.Configure(
                "\uE8A5",
                "暂时没有日志",
                $"{item.Name} 正在运行，但还没有发现日志文件。可以立即刷新，或回到连接详情检查状态。",
                "刷新日志",
                "查看连接详情");
            _logsEmptyState.PrimaryAction = RefreshLogFromEmptyStateAsync;
            _logsEmptyState.SecondaryAction = NavigateToConnectionsAsync;
        }
        else
        {
            _logsEmptyState.Configure(
                "\uE8A5",
                "暂无运行日志",
                $"{item.Name} 当前未运行。启动连接后，日志会自动显示在这里。",
                "启动连接",
                "查看连接详情");
            _logsEmptyState.PrimaryAction = StartSelectedFromEmptyStateAsync;
            _logsEmptyState.SecondaryAction = NavigateToConnectionsAsync;
        }

        ShowEmptyState(LogsPage, _logsEmptyState, true);
    }

    private static void ShowEmptyState(Grid page, EmptyStateControl state, bool show)
    {
        foreach (var child in page.Children)
        {
            if (ReferenceEquals(child, state)) continue;
            child.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        }
        state.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyInitialReadinessNavigation(ApplicationReadinessState readiness)
    {
        if (_initialReadinessNavigationApplied || !_initialized || ViewModel.IsBusy) return;
        _initialReadinessNavigationApplied = true;
        if (readiness == ApplicationReadinessState.Ready) return;

        DispatcherQueue.TryEnqueue(() => SelectPage("dashboard"));
    }

    private Task NavigateToSettingsAsync()
    {
        SelectPage("settings");
        return Task.CompletedTask;
    }

    private Task NavigateToConnectionsAsync()
    {
        SelectPage("connections");
        return Task.CompletedTask;
    }

    private Task PickTunnelClientFromEmptyStateAsync()
    {
        SelectPage("settings");
        BrowseTunnelClient_Click(this, null!);
        return Task.CompletedTask;
    }

    private Task CreateProfileFromEmptyStateAsync()
    {
        SelectPage("connections");
        AddProfile_Click(this, null!);
        return Task.CompletedTask;
    }

    private async Task RefreshFromEmptyStateAsync()
    {
        await ViewModel.RefreshAsync();
        UpdateEmptyStates();
    }

    private async Task RefreshLogFromEmptyStateAsync()
    {
        await ViewModel.RefreshLogIncrementalAsync();
        UpdateEmptyStates();
    }

    private async Task StartSelectedFromEmptyStateAsync()
    {
        if (ViewModel.StartSelectedCommand.CanExecute(null))
        {
            await ViewModel.StartSelectedCommand.ExecuteAsync(null);
        }
        UpdateEmptyStates();
    }
}
