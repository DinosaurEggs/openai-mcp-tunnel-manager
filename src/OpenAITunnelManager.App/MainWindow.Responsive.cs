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

        ConfigureHeader(ConnectionsHeader, ConnectionsHeaderTitle, ConnectionsHeaderActions, width < 760);
        ConfigureHeader(LogsHeader, LogsHeaderTitle, LogsHeaderActions, width < 760);
        ConfigureHeader(DiagnosticsHeader, DiagnosticsHeaderTitle, DiagnosticsHeaderActions, width < 760);

        ApplyConnectionsLayout(bucket);
        ApplyLogsLayout(bucket);
        ApplyDiagnosticsLayout(bucket);
        ApplyDashboardLayout(width);
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
        scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
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
            ConnectionsSplitGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32, GridUnitType.Star) });
            ConnectionsSplitGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(68, GridUnitType.Star) });
            Grid.SetRow(ConnectionsListPanel, 0);
            Grid.SetColumn(ConnectionsListPanel, 0);
            Grid.SetRow(ConnectionsDetailsPanel, 1);
            Grid.SetColumn(ConnectionsDetailsPanel, 0);
        }
        else
        {
            ConnectionsSplitGrid.RowSpacing = 0;
            ConnectionsSplitGrid.ColumnSpacing = 12;
            var listShare = bucket == "compact" ? 35 : 30;
            ConnectionsSplitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(listShare, GridUnitType.Star) });
            ConnectionsSplitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - listShare, GridUnitType.Star) });
            Grid.SetRow(ConnectionsListPanel, 0);
            Grid.SetColumn(ConnectionsListPanel, 0);
            Grid.SetRow(ConnectionsDetailsPanel, 0);
            Grid.SetColumn(ConnectionsDetailsPanel, 1);
        }

        var stackActions = bucket != "wide";
        ConnectionsPrimaryActions.Orientation = stackActions ? Orientation.Vertical : Orientation.Horizontal;
        ConnectionsPrimaryActions.HorizontalAlignment = stackActions ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        foreach (var button in ConnectionsPrimaryActions.Children.OfType<Button>())
        {
            button.HorizontalAlignment = stackActions ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        }
    }

    private void ApplyLogsLayout(string bucket)
    {
        var controls = LogsFilterGrid.Children.OfType<FrameworkElement>().Take(5).ToArray();
        LogsFilterGrid.RowDefinitions.Clear();
        LogsFilterGrid.ColumnDefinitions.Clear();
        LogsFilterGrid.ColumnSpacing = 10;
        LogsFilterGrid.RowSpacing = bucket == "wide" ? 0 : 8;

        if (bucket == "wide")
        {
            foreach (var width in new[] { 30d, 28d, 16d, 13d, 13d })
            {
                LogsFilterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width, GridUnitType.Star) });
            }
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
        Grid.SetRow(controls[3], 2); Grid.SetColumn(controls[3], 0); Grid.SetColumnSpan(controls[3], 1);
        Grid.SetRow(controls[4], 2); Grid.SetColumn(controls[4], 1); Grid.SetColumnSpan(controls[4], 1);
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

    private void ApplyDashboardLayout(double width)
    {
        var cards = DashboardCardsGrid.Children.OfType<Border>().ToArray();
        DashboardCardsGrid.RowDefinitions.Clear();
        DashboardCardsGrid.ColumnDefinitions.Clear();
        if (width >= 800)
        {
            DashboardCardsGrid.RowSpacing = 0;
            DashboardCardsGrid.ColumnSpacing = 12;
            for (var index = 0; index < cards.Length; index++)
            {
                DashboardCardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetRow(cards[index], 0);
                Grid.SetColumn(cards[index], index);
            }
        }
        else
        {
            DashboardCardsGrid.ColumnSpacing = 0;
            DashboardCardsGrid.RowSpacing = 12;
            DashboardCardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var index = 0; index < cards.Length; index++)
            {
                DashboardCardsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(cards[index], index);
                Grid.SetColumn(cards[index], 0);
            }
        }
    }

    private void ApplySettingsLayout(string bucket)
    {
        SettingsClientGrid.RowDefinitions.Clear();
        SettingsClientGrid.ColumnDefinitions.Clear();
        if (bucket == "narrow")
        {
            SettingsClientGrid.RowSpacing = 8;
            SettingsClientGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            SettingsClientGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            SettingsClientGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            SettingsClientGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var children = SettingsClientGrid.Children.OfType<FrameworkElement>().ToArray();
            if (children.Length >= 3)
            {
                Grid.SetRow(children[0], 0); Grid.SetColumn(children[0], 0); Grid.SetColumnSpan(children[0], 2);
                Grid.SetRow(children[1], 1); Grid.SetColumn(children[1], 0); Grid.SetColumnSpan(children[1], 1);
                Grid.SetRow(children[2], 1); Grid.SetColumn(children[2], 1); Grid.SetColumnSpan(children[2], 1);
            }
            return;
        }

        SettingsClientGrid.RowSpacing = 0;
        SettingsClientGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28, GridUnitType.Star) });
        SettingsClientGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72, GridUnitType.Star) });
        SettingsClientGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var normalChildren = SettingsClientGrid.Children.OfType<FrameworkElement>().ToArray();
        for (var index = 0; index < normalChildren.Length; index++)
        {
            Grid.SetRow(normalChildren[index], 0);
            Grid.SetColumn(normalChildren[index], index);
            Grid.SetColumnSpan(normalChildren[index], 1);
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

    // Non-responsive auxiliary UI helpers still use this generic tree walk (for example the
    // advanced directory-picker enhancer). Responsive layout itself uses only named XAML elements.
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
