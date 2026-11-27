using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenAITunnelManager.App.Diagnostics;

namespace OpenAITunnelManager.App;

public sealed partial class MainWindow
{
    private bool _responsiveLayoutEnabled;
    private string _lastResponsiveLayoutBucket = string.Empty;
    private (bool SummaryWide, int ToolbarColumns, int PrimaryColumns, bool FiltersWide, int OptionColumns, bool HeaderCompact, bool DiagnosticsWide, bool DiagnosticsHeaderCompact)? _lastDetailLayout;
    private (bool Wide, bool CompactScope, int ActionColumns)? _lastRemoteLayout;

    internal void EnableResponsiveLayout()
    {
        if (_responsiveLayoutEnabled) return;
        _responsiveLayoutEnabled = true;
        RootGrid.SizeChanged += ResponsiveRoot_SizeChanged;
        ContentGrid.SizeChanged += ResponsiveRoot_SizeChanged;
        ConnectionsDetailsPanel.SizeChanged += ResponsiveDetails_SizeChanged;
        RemotePage.SizeChanged += ResponsiveRemote_SizeChanged;
        RootGrid.Loaded += ResponsiveRoot_Loaded;
        Navigation.SelectionChanged += ResponsiveNavigation_SelectionChanged;
        RequestResponsiveLayout();
    }

    private void ResponsiveRoot_Loaded(object sender, RoutedEventArgs e) => RequestResponsiveLayout();
    private void ResponsiveRoot_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyResponsiveLayout();
    private void ResponsiveDetails_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 1) ApplyDetailLayout();
    }
    private void ResponsiveRemote_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyRemoteLayout();
    private void ResponsiveNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args) => RequestResponsiveLayout();

    private void RequestResponsiveLayout()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ApplyResponsiveLayout();
            DispatcherQueue.TryEnqueue(ApplyResponsiveLayout);
        });
    }

    private void ApplyResponsiveLayout()
    {
        StretchScrollablePage(DashboardScrollViewer, DashboardContentPanel);
        StretchScrollablePage(SettingsScrollViewer, SettingsContentPanel);

        var width = ContentGrid.ActualWidth;
        if (width <= 1) width = Math.Max(0, RootGrid.ActualWidth - 64);
        if (width <= 1) return;

        var bucket = width switch
        {
            < 650 => "narrow",
            < 1000 => "compact",
            _ => "wide"
        };
        if (!string.Equals(bucket, _lastResponsiveLayoutBucket, StringComparison.Ordinal))
        {
            _lastResponsiveLayoutBucket = bucket;
            AppLog.Info($"Responsive layout changed | mode={bucket} | contentWidth={width:F0}epx | windowWidth={RootGrid.ActualWidth:F0}epx");
        }

        ContentGrid.Padding = bucket switch
        {
            "narrow" => new Thickness(10, 12, 10, 10),
            "compact" => new Thickness(16, 16, 16, 12),
            _ => new Thickness(24, 20, 24, 16)
        };

        ConfigureHeader(DashboardHeader, DashboardHeaderTitle, DashboardHeaderActions, width < 760);
        ConfigureHeader(ConnectionsHeader, ConnectionsHeaderTitle, ConnectionsHeaderActions, width < 760);
        ApplyConnectionsLayout(bucket);
        ApplyDetailLayout();
        ApplyRemoteLayout();
        ApplyDashboardLayout(bucket);
        ApplySettingsLayout(bucket);

        if (RootGrid.ActualWidth < 1080) Navigation.IsPaneOpen = false;
    }

    private static void StretchScrollablePage(ScrollViewer scrollViewer, StackPanel panel)
    {
        panel.MaxWidth = double.PositiveInfinity;
        panel.Width = double.NaN;
        panel.HorizontalAlignment = HorizontalAlignment.Stretch;
        scrollViewer.HorizontalAlignment = HorizontalAlignment.Stretch;
        scrollViewer.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        scrollViewer.HorizontalScrollMode = ScrollMode.Disabled;
        scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        scrollViewer.ZoomMode = ZoomMode.Disabled;
    }

    private void ApplyConnectionsLayout(string bucket)
    {
        ConnectionsSplitGrid.ColumnDefinitions.Clear();
        ConnectionsSplitGrid.RowDefinitions.Clear();

        if (bucket == "narrow")
        {
            ConnectionsSplitGrid.ColumnSpacing = 0;
            ConnectionsSplitGrid.RowSpacing = 12;
            ConnectionsSplitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ConnectionsSplitGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star) });
            ConnectionsSplitGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(7, GridUnitType.Star) });
            Grid.SetRow(ConnectionsListPanel, 0);
            Grid.SetColumn(ConnectionsListPanel, 0);
            Grid.SetRow(ConnectionsDetailsPanel, 1);
            Grid.SetColumn(ConnectionsDetailsPanel, 0);
        }
        else
        {
            ConnectionsSplitGrid.RowSpacing = 0;
            ConnectionsSplitGrid.ColumnSpacing = 12;
            if (bucket == "compact")
            {
                ConnectionsSplitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(9, GridUnitType.Star), MaxWidth = 280 });
                ConnectionsSplitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16, GridUnitType.Star) });
            }
            else
            {
                ConnectionsSplitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star), MaxWidth = 340 });
                ConnectionsSplitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(7, GridUnitType.Star) });
            }
            Grid.SetRow(ConnectionsListPanel, 0);
            Grid.SetColumn(ConnectionsListPanel, 0);
            Grid.SetRow(ConnectionsDetailsPanel, 0);
            Grid.SetColumn(ConnectionsDetailsPanel, 1);
        }
    }

    private void ApplyDetailLayout()
    {
        var width = ConnectionsDetailsPanel.ActualWidth - 36;
        if (width <= 1) return;
        var layout = (
            SummaryWide: width >= 800,
            ToolbarColumns: width >= 850 ? 8 : width >= 440 ? 4 : width >= 240 ? 2 : 1,
            PrimaryColumns: width >= 440 ? 5 : width >= 280 ? 3 : 2,
            FiltersWide: width >= 800,
            OptionColumns: width >= 360 ? 3 : width >= 240 ? 2 : 1,
            HeaderCompact: width < 1100,
            DiagnosticsWide: width >= 800,
            DiagnosticsHeaderCompact: width < 540);
        if (_lastDetailLayout == layout) return;
        _lastDetailLayout = layout;

        ConfigureHeader(LogsHeader, LogsHeaderTitle, LogsHeaderActions, layout.HeaderCompact);
        ArrangeControls(LogsHeaderActions, layout.ToolbarColumns);
        ArrangeControls(ConnectionsPrimaryActions, layout.PrimaryColumns);
        ConfigureHeader(DiagnosticsHeader, DiagnosticsHeaderTitle, DiagnosticsHeaderActions, layout.DiagnosticsHeaderCompact);

        ConnectionSummaryGrid.ColumnDefinitions.Clear();
        ConnectionSummaryGrid.RowDefinitions.Clear();
        ConnectionSummaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (layout.SummaryWide)
        {
            ConnectionSummaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(ConnectionSummaryRight, 0); Grid.SetColumn(ConnectionSummaryRight, 1);
        }
        else
        {
            ConnectionSummaryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ConnectionSummaryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(ConnectionSummaryRight, 1); Grid.SetColumn(ConnectionSummaryRight, 0);
        }

        ApplyLogsLayout(layout.FiltersWide, layout.OptionColumns);
        ApplyDiagnosticsLayout(layout.DiagnosticsWide ? "wide" : "compact");
    }

    private static void ArrangeControls(Grid grid, int columns)
    {
        var children = grid.Children.OfType<FrameworkElement>().ToArray();
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();
        for (var i = 0; i < columns; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var i = 0; i < (children.Length + columns - 1) / columns; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < children.Length; i++)
        {
            Grid.SetRow(children[i], i / columns);
            Grid.SetColumn(children[i], i % columns);
        }
    }

    private void ApplyLogsLayout(bool wide, int optionColumns)
    {
        LogsFilterGrid.RowDefinitions.Clear();
        LogsFilterGrid.ColumnDefinitions.Clear();
        if (wide)
        {
            LogsFilterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            LogsFilterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            LogsFilterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            for (var i = 0; i < 2; i++) LogsFilterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(LogSearchPanel, 0); Grid.SetColumn(LogSearchPanel, 0); Grid.SetColumnSpan(LogSearchPanel, 1);
            Grid.SetRow(LogLevelCombo, 0); Grid.SetColumn(LogLevelCombo, 1);
            Grid.SetRow(LogComponentBox, 0); Grid.SetColumn(LogComponentBox, 2);
            Grid.SetRow(LogOptionsGrid, 1); Grid.SetColumnSpan(LogOptionsGrid, 3);
        }
        else
        {
            LogsFilterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            LogsFilterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < 3; i++) LogsFilterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(LogSearchPanel, 0); Grid.SetColumn(LogSearchPanel, 0); Grid.SetColumnSpan(LogSearchPanel, 2);
            Grid.SetRow(LogLevelCombo, 1); Grid.SetColumn(LogLevelCombo, 0);
            Grid.SetRow(LogComponentBox, 1); Grid.SetColumn(LogComponentBox, 1);
            Grid.SetRow(LogOptionsGrid, 2); Grid.SetColumnSpan(LogOptionsGrid, 2);
        }
        ArrangeControls(LogOptionsGrid, optionColumns);

        var searchButtons = LogSearchPanel.Children.OfType<FrameworkElement>().Where(child => child != LogSearchBox).ToArray();
        LogSearchPanel.ColumnDefinitions.Clear();
        LogSearchPanel.RowDefinitions.Clear();
        if (optionColumns < 3)
        {
            for (var i = 0; i < searchButtons.Length; i++)
                LogSearchPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            LogSearchPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            LogSearchPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumnSpan(LogSearchBox, searchButtons.Length);
            for (var i = 0; i < searchButtons.Length; i++)
            {
                Grid.SetRow(searchButtons[i], 1);
                Grid.SetColumn(searchButtons[i], i);
            }
        }
        else
        {
            LogSearchPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumnSpan(LogSearchBox, 1);
            for (var i = 0; i < searchButtons.Length; i++)
            {
                LogSearchPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetRow(searchButtons[i], 0);
                Grid.SetColumn(searchButtons[i], i + 1);
            }
        }
    }

    private void ApplyRemoteLayout()
    {
        var width = RemotePage.ActualWidth;
        var height = RemotePage.ActualHeight;
        if (width <= 1 || height <= 1) return;
        var layout = (Wide: width >= 800, CompactScope: width < 440, ActionColumns: width >= 360 ? 3 : 2);
        // Give the inventory and result panes the remaining viewport height on wide screens.
        // Narrow or short windows scroll without clipping the query controls.
        RemoteContentGrid.Height = layout.Wide ? Math.Max(540, height) : double.NaN;
        if (_lastRemoteLayout == layout) return;
        _lastRemoteLayout = layout;

        foreach (var (grid, second) in new[]
        {
            (RemoteQueryGrid, RemoteScopePanel),
            (RemoteLayoutGrid, RemoteDetailsPanel)
        })
        {
            grid.ColumnDefinitions.Clear();
            grid.RowDefinitions.Clear();
            if (layout.Wide)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36, GridUnitType.Star), MaxWidth = 440 });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64, GridUnitType.Star) });
                Grid.SetRow(second, 0); Grid.SetColumn(second, 1);
            }
            else
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(second, 1); Grid.SetColumn(second, 0);
            }
        }

        RemoteTunnelList.Height = layout.Wide ? double.NaN : 180;
        RemoteResultText.Height = layout.Wide ? double.NaN : 280;
        ArrangeControls(RemoteDetailActions, layout.ActionColumns);

        RemoteScopeFields.ColumnDefinitions.Clear();
        RemoteScopeFields.RowDefinitions.Clear();
        if (layout.CompactScope)
        {
            RemoteScopeFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < 3; i++) RemoteScopeFields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(RemoteScopeId, 1); Grid.SetColumn(RemoteScopeId, 0);
            Grid.SetRow(RemoteAdminSecret, 2); Grid.SetColumnSpan(RemoteAdminSecret, 1);
        }
        else
        {
            RemoteScopeFields.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            RemoteScopeFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            RemoteScopeFields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RemoteScopeFields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(RemoteScopeId, 0); Grid.SetColumn(RemoteScopeId, 1);
            Grid.SetRow(RemoteAdminSecret, 1); Grid.SetColumnSpan(RemoteAdminSecret, 2);
        }
    }

    private void ApplyDiagnosticsLayout(string bucket)
    {
        DiagnosticsBody.RowDefinitions.Clear();
        DiagnosticsBody.ColumnDefinitions.Clear();
        if (bucket == "wide")
        {
            DiagnosticsBody.RowSpacing = 0;
            DiagnosticsBody.ColumnSpacing = 12;
            DiagnosticsBody.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            DiagnosticsBody.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(DiagnosticsDoctorPanel, 0); Grid.SetColumn(DiagnosticsDoctorPanel, 0);
            Grid.SetRow(DiagnosticsHealthPanel, 0); Grid.SetColumn(DiagnosticsHealthPanel, 1);
        }
        else
        {
            DiagnosticsBody.ColumnSpacing = 0;
            DiagnosticsBody.RowSpacing = 12;
            DiagnosticsBody.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            DiagnosticsBody.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            DiagnosticsBody.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(DiagnosticsDoctorPanel, 0); Grid.SetColumn(DiagnosticsDoctorPanel, 0);
            Grid.SetRow(DiagnosticsHealthPanel, 1); Grid.SetColumn(DiagnosticsHealthPanel, 0);
        }
    }

    private void ApplyDashboardLayout(string bucket)
    {
        DashboardContentPanel.Width = double.NaN;
        DashboardContentPanel.MaxWidth = double.PositiveInfinity;
        DashboardContentPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
        DashboardScrollViewer.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        DashboardScrollViewer.HorizontalScrollMode = ScrollMode.Disabled;
        DashboardScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;

        var cards = DashboardCardsGrid.Children.OfType<Border>().ToArray();
        DashboardCardsGrid.Width = double.NaN;
        DashboardCardsGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
        DashboardCardsGrid.RowDefinitions.Clear();
        DashboardCardsGrid.ColumnDefinitions.Clear();
        foreach (var card in cards)
        {
            card.MinWidth = 0;
            card.HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        if (bucket == "wide")
        {
            DashboardCardsGrid.RowSpacing = 0;
            DashboardCardsGrid.ColumnSpacing = 12;
            for (var index = 0; index < cards.Length; index++)
            {
                DashboardCardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetRow(cards[index], 0);
                Grid.SetColumn(cards[index], index);
                Grid.SetColumnSpan(cards[index], 1);
            }
            return;
        }

        if (bucket == "compact")
        {
            DashboardCardsGrid.ColumnSpacing = 12;
            DashboardCardsGrid.RowSpacing = 12;
            DashboardCardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            DashboardCardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            DashboardCardsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            DashboardCardsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var index = 0; index < cards.Length; index++)
            {
                Grid.SetRow(cards[index], index / 2);
                Grid.SetColumn(cards[index], index % 2);
                Grid.SetColumnSpan(cards[index], 1);
            }
            return;
        }

        DashboardCardsGrid.ColumnSpacing = 0;
        DashboardCardsGrid.RowSpacing = 8;
        DashboardCardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var index = 0; index < cards.Length; index++)
        {
            DashboardCardsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(cards[index], index);
            Grid.SetColumn(cards[index], 0);
            Grid.SetColumnSpan(cards[index], 1);
        }
    }

    private void ApplySettingsLayout(string bucket)
    {
        ApplyDirectoryOverrideLayout(bucket == "narrow");

        SettingsClientGrid.RowDefinitions.Clear();
        SettingsClientGrid.ColumnDefinitions.Clear();

        if (bucket == "narrow")
        {
            SettingsClientGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            SettingsClientGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            SettingsClientGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            SettingsClientGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid.SetRow(SettingsClientSummary, 0);
            Grid.SetColumn(SettingsClientSummary, 0);
            Grid.SetRow(SettingsClientActions, 1);
            Grid.SetColumn(SettingsClientActions, 0);
            Grid.SetRow(TunnelClientProgressPanel, 2);
            Grid.SetColumn(TunnelClientProgressPanel, 0);
            Grid.SetColumnSpan(TunnelClientProgressPanel, 1);
            SettingsClientActions.HorizontalAlignment = HorizontalAlignment.Left;
            return;
        }

        SettingsClientGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        SettingsClientGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        SettingsClientGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        SettingsClientGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(SettingsClientSummary, 0);
        Grid.SetColumn(SettingsClientSummary, 0);
        Grid.SetRow(SettingsClientActions, 0);
        Grid.SetColumn(SettingsClientActions, 1);
        Grid.SetRow(TunnelClientProgressPanel, 1);
        Grid.SetColumn(TunnelClientProgressPanel, 0);
        Grid.SetColumnSpan(TunnelClientProgressPanel, 2);
        SettingsClientActions.HorizontalAlignment = HorizontalAlignment.Right;
    }

    private void ApplyDirectoryOverrideLayout(bool narrow)
    {
        foreach (var grid in FindDescendants<Grid>(SettingsContentPanel))
        {
            var buttons = grid.Children
                .OfType<Button>()
                .Where(static button => button.Tag is string tag && (tag == "profile" || tag == "state"))
                .ToArray();
            var textBox = grid.Children.OfType<TextBox>().FirstOrDefault();
            if (buttons.Length != 2 || textBox is null) continue;

            grid.RowDefinitions.Clear();
            grid.ColumnDefinitions.Clear();
            grid.ColumnSpacing = 8;

            if (narrow)
            {
                grid.RowSpacing = 8;
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(textBox, 0); Grid.SetColumn(textBox, 0); Grid.SetColumnSpan(textBox, 2);
                for (var index = 0; index < buttons.Length; index++)
                {
                    Grid.SetRow(buttons[index], 1);
                    Grid.SetColumn(buttons[index], index);
                    Grid.SetColumnSpan(buttons[index], 1);
                    buttons[index].HorizontalAlignment = HorizontalAlignment.Stretch;
                }
                continue;
            }

            grid.RowSpacing = 0;
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(textBox, 0); Grid.SetColumn(textBox, 0); Grid.SetColumnSpan(textBox, 1);
            for (var index = 0; index < buttons.Length; index++)
            {
                Grid.SetRow(buttons[index], 0);
                Grid.SetColumn(buttons[index], index + 1);
                Grid.SetColumnSpan(buttons[index], 1);
                buttons[index].HorizontalAlignment = HorizontalAlignment.Left;
            }
        }
    }

    private static void ConfigureHeader(Grid header, StackPanel titlePanel, FrameworkElement actionPanel, bool compact)
    {
        header.RowDefinitions.Clear();
        header.ColumnDefinitions.Clear();
        if (!compact)
        {
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(titlePanel, 0); Grid.SetColumn(titlePanel, 0);
            Grid.SetRow(actionPanel, 0); Grid.SetColumn(actionPanel, 1);
            actionPanel.HorizontalAlignment = HorizontalAlignment.Right;
            actionPanel.Margin = new Thickness(0);
            return;
        }

        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(titlePanel, 0); Grid.SetColumn(titlePanel, 0);
        Grid.SetRow(actionPanel, 1); Grid.SetColumn(actionPanel, 0);
        actionPanel.HorizontalAlignment = HorizontalAlignment.Left;
        actionPanel.Margin = new Thickness(0, 10, 0, 0);
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var nested in FindDescendants<T>(child)) yield return nested;
        }
    }
}
