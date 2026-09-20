using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenAITunnelManager.App.Diagnostics;

namespace OpenAITunnelManager.App;

public sealed partial class MainWindow
{
    private bool _responsiveLayoutEnabled;
    private string _lastResponsiveLayoutBucket = string.Empty;

    internal void EnableResponsiveLayout()
    {
        if (_responsiveLayoutEnabled) return;
        _responsiveLayoutEnabled = true;
        RootGrid.SizeChanged += ResponsiveRoot_SizeChanged;
        RootGrid.Loaded += ResponsiveRoot_Loaded;
        Navigation.SelectionChanged += ResponsiveNavigation_SelectionChanged;
        RequestResponsiveLayout();
    }

    private void ResponsiveRoot_Loaded(object sender, RoutedEventArgs e) => RequestResponsiveLayout();
    private void ResponsiveRoot_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyResponsiveLayout();
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
        ConfigureHeader(LogsHeader, LogsHeaderTitle, LogsHeaderActions, width < 860);
        ConfigureHeader(DiagnosticsHeader, DiagnosticsHeaderTitle, DiagnosticsHeaderActions, width < 860);

        ApplyConnectionsLayout(bucket);
        ApplyLogsLayout(bucket);
        ApplyDiagnosticsLayout(bucket);
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
                ConnectionsSplitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(9, GridUnitType.Star) });
                ConnectionsSplitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16, GridUnitType.Star) });
            }
            else
            {
                ConnectionsSplitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
                ConnectionsSplitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(7, GridUnitType.Star) });
            }
            Grid.SetRow(ConnectionsListPanel, 0);
            Grid.SetColumn(ConnectionsListPanel, 0);
            Grid.SetRow(ConnectionsDetailsPanel, 0);
            Grid.SetColumn(ConnectionsDetailsPanel, 1);
        }

        var stackActions = bucket == "narrow";
        ConnectionsPrimaryActions.Orientation = stackActions ? Orientation.Vertical : Orientation.Horizontal;
        ConnectionsPrimaryActions.HorizontalAlignment = stackActions ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        foreach (var button in ConnectionsPrimaryActions.Children.OfType<Button>())
            button.HorizontalAlignment = stackActions ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
    }

    private void ApplyLogsLayout(string bucket)
    {
        var controls = LogsFilterGrid.Children.OfType<FrameworkElement>().Take(4).ToArray();
        if (controls.Length < 4) return;

        LogsFilterGrid.RowDefinitions.Clear();
        LogsFilterGrid.ColumnDefinitions.Clear();
        LogsFilterGrid.ColumnSpacing = 10;
        LogsFilterGrid.RowSpacing = bucket == "wide" ? 0 : 8;

        if (bucket == "wide")
        {
            foreach (var width in new[] { 42d, 22d, 18d, 18d })
                LogsFilterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width, GridUnitType.Star) });
            for (var index = 0; index < controls.Length; index++)
            {
                Grid.SetRow(controls[index], 0);
                Grid.SetColumn(controls[index], index);
                Grid.SetColumnSpan(controls[index], 1);
            }
            return;
        }

        if (bucket == "narrow")
        {
            LogsFilterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var index = 0; index < controls.Length; index++)
            {
                LogsFilterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(controls[index], index);
                Grid.SetColumn(controls[index], 0);
                Grid.SetColumnSpan(controls[index], 1);
            }
            return;
        }

        LogsFilterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        LogsFilterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        LogsFilterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        LogsFilterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        LogsFilterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(controls[0], 0); Grid.SetColumn(controls[0], 0); Grid.SetColumnSpan(controls[0], 2);
        Grid.SetRow(controls[1], 1); Grid.SetColumn(controls[1], 0); Grid.SetColumnSpan(controls[1], 1);
        Grid.SetRow(controls[2], 1); Grid.SetColumn(controls[2], 1); Grid.SetColumnSpan(controls[2], 1);
        Grid.SetRow(controls[3], 2); Grid.SetColumn(controls[3], 0); Grid.SetColumnSpan(controls[3], 2);
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
            SettingsClientGrid.RowSpacing = 8;
            SettingsClientGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            SettingsClientGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            SettingsClientGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            SettingsClientGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            SettingsClientGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var children = SettingsClientGrid.Children.OfType<FrameworkElement>().ToArray();
            if (children.Length >= 4)
            {
                Grid.SetRow(children[0], 0); Grid.SetColumn(children[0], 0); Grid.SetColumnSpan(children[0], 3);
                Grid.SetRow(children[1], 1); Grid.SetColumn(children[1], 0); Grid.SetColumnSpan(children[1], 1);
                Grid.SetRow(children[2], 1); Grid.SetColumn(children[2], 1); Grid.SetColumnSpan(children[2], 1);
                Grid.SetRow(children[3], 1); Grid.SetColumn(children[3], 2); Grid.SetColumnSpan(children[3], 1);
            }
            return;
        }

        SettingsClientGrid.RowSpacing = 0;
        SettingsClientGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28, GridUnitType.Star) });
        SettingsClientGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72, GridUnitType.Star) });
        SettingsClientGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        SettingsClientGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var normalChildren = SettingsClientGrid.Children.OfType<FrameworkElement>().ToArray();
        for (var index = 0; index < normalChildren.Length; index++)
        {
            Grid.SetRow(normalChildren[index], 0);
            Grid.SetColumn(normalChildren[index], index);
            Grid.SetColumnSpan(normalChildren[index], 1);
        }
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

    private static void ConfigureHeader(Grid header, StackPanel titlePanel, StackPanel actionPanel, bool compact)
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
