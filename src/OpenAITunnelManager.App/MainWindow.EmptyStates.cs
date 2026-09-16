using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenAITunnelManager.App.Controls;
using OpenAITunnelManager.App.ViewModels;
using Windows.Storage.Pickers;

namespace OpenAITunnelManager.App;

public sealed partial class MainWindow
{
    private bool _emptyStatesEnabled;
    private EmptyStateControl? _dashboardEmptyState;
    private EmptyStateControl? _connectionsEmptyState;

    internal void EnableEmptyStates()
    {
        if (_emptyStatesEnabled) return;
        _emptyStatesEnabled = true;

        _dashboardEmptyState = AttachEmptyState(DashboardPage);
        _connectionsEmptyState = AttachEmptyState(ConnectionsPage);

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

    private void ViewModel_PropertyChangedForEmptyStates(object? sender, PropertyChangedEventArgs e) =>
        DispatcherQueue.TryEnqueue(UpdateEmptyStates);

    private void Connections_CollectionChangedForEmptyStates(object? sender, NotifyCollectionChangedEventArgs e) =>
        DispatcherQueue.TryEnqueue(UpdateEmptyStates);

    private void UpdateEmptyStates()
    {
        if (!_emptyStatesEnabled || _dashboardEmptyState is null || _connectionsEmptyState is null) return;

        var readiness = ViewModel.ReadinessState;
        switch (readiness)
        {
            case ApplicationReadinessState.TunnelClientNotConfigured:
                ConfigureClientMissingStates(invalidPath: false);
                ShowEmptyState(DashboardPage, _dashboardEmptyState, true);
                ShowEmptyState(ConnectionsPage, _connectionsEmptyState, true);
                break;

            case ApplicationReadinessState.TunnelClientInvalid:
                ConfigureClientMissingStates(invalidPath: true);
                ShowEmptyState(DashboardPage, _dashboardEmptyState, true);
                ShowEmptyState(ConnectionsPage, _connectionsEmptyState, true);
                break;

            case ApplicationReadinessState.NoConnections:
                ConfigureNoConnectionsStates();
                ShowEmptyState(DashboardPage, _dashboardEmptyState, true);
                ShowEmptyState(ConnectionsPage, _connectionsEmptyState, true);
                break;

            default:
                ShowEmptyState(DashboardPage, _dashboardEmptyState, false);
                ShowEmptyState(ConnectionsPage, _connectionsEmptyState, false);
                break;
        }

        MissingClientInfo.IsOpen = readiness is ApplicationReadinessState.TunnelClientNotConfigured or ApplicationReadinessState.TunnelClientInvalid;
    }

    private void ConfigureClientMissingStates(bool invalidPath)
    {
        var title = invalidPath ? "tunnel-client 配置无效" : "尚未配置 tunnel-client";
        var description = invalidPath
            ? "当前 tunnel-client 路径不可用。重新选择 tunnel-client.exe 后，Manager 会读取已有 Profile 和 Runtime。"
            : "Manager 需要通过 tunnel-client 读取和管理 Profile、Runtime、日志与诊断信息。先完成基础配置即可继续。";

        ConfigureClientRequiredState(_dashboardEmptyState!, "\uE713", title, description);
        ConfigureClientRequiredState(_connectionsEmptyState!, "\uE71B", title, description);
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

    private Task NavigateToSettingsAsync()
    {
        SelectPage("settings");
        return Task.CompletedTask;
    }

    private async Task PickTunnelClientFromEmptyStateAsync()
    {
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
            picker.FileTypeFilter.Add(".exe");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            await ViewModel.ApplyTunnelClientPathAsync(file.Path);
            UpdateEmptyStates();
        }
        catch (Exception exception)
        {
            SelectPage("settings");
            await ShowErrorAsync($"配置 tunnel-client 失败：{exception.Message}");
        }
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
}
