using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenAITunnelManager.App.Diagnostics;

namespace OpenAITunnelManager.App;

public sealed partial class MainWindow
{
    private bool _responsiveLayoutEnabled;
    private string _lastResponsiveLayoutBucket = string.Empty;
    private Grid? _contentGrid;
    private Grid? _connectionsHeader;
    private StackPanel? _connectionsHeaderTitle;
    private StackPanel? _connectionsHeaderActions;
    private Grid? _connectionsSplitGrid;
    private Border? _connectionsListPanel;
    private Border? _connectionsDetailsPanel;
    private StackPanel? _connectionsPrimaryActions;
    private Grid? _logsHeader;
    private StackPanel? _logsHeaderTitle;
    private StackPanel? _logsHeaderActions;
    private Grid? _logsFilterGrid;
    private Grid? _diagnosticsHeader;
    private StackPanel? _diagnosticsHeaderTitle;
    private StackPanel? _diagnosticsHeaderActions;
    private Grid? _diagnosticsBody;
    private Border? _diagnosticsDoctorPanel;
    private Border? _diagnosticsHealthPanel;
    private Grid? _dashboardCardsGrid;
    private StackPanel? _dashboardContentPanel;
    private StackPanel? _settingsContentPanel;
    private Grid? _settingsClientGrid;
    private Grid? _settingsRefreshGrid;
    private NumberBox? _settingsRefreshNumberBox;

    internal void EnableResponsiveLayout()
    {
        if (_responsiveLayoutEnabled) return;
        _responsiveLayoutEnabled = true;

        RootGrid.SizeChanged += ResponsiveRoot_SizeChanged;
        RootGrid.Loaded += ResponsiveRoot_Loaded;
        DispatcherQueue.TryEnqueue(ApplyResponsiveLayout);
    }

    private void ResponsiveRoot_Loaded(object sender, RoutedEventArgs e) => ApplyResponsiveLayout();

    private void ResponsiveRoot_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyResponsiveLayout();

    private void ApplyResponsiveLayout()
    {
        CacheResponsiveElements();

        var contentWidth = ConnectionsPage.ActualWidth;
        if (contentWidth <= 1) contentWidth = Math.Max(0, RootGrid.ActualWidth - 64);
        if (contentWidth <= 1) return;

        var stackedConnections = contentWidth < 900;
        var narrow = contentWidth < 620;
        var veryNarrow = contentWidth < 520;
        var bucket = contentWidth switch
        {
            < 520 => "very-narrow",
            < 620 => "narrow",
            < 900 => "stacked",
            _ => "wide"
        };

        if (!string.Equals(bucket, _lastResponsiveLayoutBucket, StringComparison.Ordinal))
        {
            _lastResponsiveLayoutBucket = bucket;
            AppLog.Info($"Responsive layout changed | mode={bucket} | contentWidth={contentWidth:F0}epx | windowWidth={RootGrid.ActualWidth:F0}epx");
        }

        ApplyContentPadding(contentWidth);
        ApplyWidePageAlignment();
        ApplyConnectionsLayout(contentWidth, stackedConnections);
        ApplyLogsLayout(contentWidth, veryNarrow);
        ApplyDiagnosticsLayout(contentWidth);
        ApplyDashboardLayout(contentWidth);
        ApplySettingsLayout();

        if (RootGrid.ActualWidth < 1080)
        {
            Navigation.IsPaneOpen = false;
        }
    }

    private void CacheResponsiveElements()
    {
        _contentGrid ??= VisualTreeHelper.GetParent(ConnectionsPage) as Grid;

        _dashboardContentPanel ??= FindDescendants<StackPanel>(DashboardPage)
            .FirstOrDefault(panel => panel.MaxWidth >= 1000);
        _settingsContentPanel ??= FindDescendants<StackPanel>(SettingsPage)
            .FirstOrDefault(panel => panel.MaxWidth >= 900);

        if (_settingsContentPanel is not null)
        {
            _settingsClientGrid ??= FindDescendants<Grid>(_settingsContentPanel)
                .FirstOrDefault(grid => grid.ColumnDefinitions.Count == 3);
            _settingsRefreshGrid ??= FindDescendants<Grid>(_settingsContentPanel)
                .FirstOrDefault(grid =>
                    grid.ColumnDefinitions.Count == 2 &&
                    grid.ColumnDefinitions[0].Width.IsAbsolute &&
                    grid.ColumnDefinitions[0].Width.Value >= 200);
            _settingsRefreshNumberBox ??= FindDescendants<NumberBox>(_settingsContentPanel).FirstOrDefault();
        }

        if (_connectionsHeader is null)
        {
            _connectionsHeader = ConnectionsPage.Children.OfType<Grid>().FirstOrDefault(grid => Grid.GetRow(grid) == 0);
            if (_connectionsHeader is not null)
            {
                var panels = _connectionsHeader.Children.OfType<StackPanel>().ToArray();
                _connectionsHeaderTitle = panels.FirstOrDefault();
                _connectionsHeaderActions = panels.Skip(1).FirstOrDefault();
            }
        }

        if (_connectionsListPanel is null)
        {
            _connectionsListPanel = FindAncestor<Border>(ConnectionsList);
            _connectionsSplitGrid = _connectionsListPanel is null
                ? null
                : VisualTreeHelper.GetParent(_connectionsListPanel) as Grid;
            if (_connectionsSplitGrid is not null)
            {
                _connectionsDetailsPanel = _connectionsSplitGrid.Children
                    .OfType<Border>()
                    .FirstOrDefault(border => !ReferenceEquals(border, _connectionsListPanel));
            }
        }

        if (_connectionsPrimaryActions is null && _connectionsDetailsPanel is not null)
        {
            _connectionsPrimaryActions = FindDescendants<StackPanel>(_connectionsDetailsPanel)
                .FirstOrDefault(panel => panel.Orientation == Orientation.Horizontal && panel.Children.OfType<Button>().Count() >= 5);
        }

        if (_logsHeader is null)
        {
            _logsHeader = LogsPage.Children.OfType<Grid>().FirstOrDefault(grid => Grid.GetRow(grid) == 0);
            if (_logsHeader is not null)
            {
                var panels = _logsHeader.Children.OfType<StackPanel>().ToArray();
                _logsHeaderTitle = panels.FirstOrDefault();
                _logsHeaderActions = panels.Skip(1).FirstOrDefault();
            }
            _logsFilterGrid = LogsPage.Children.OfType<Grid>().FirstOrDefault(grid => Grid.GetRow(grid) == 2);
        }

        if (_diagnosticsHeader is null)
        {
            _diagnosticsHeader = DiagnosticsPage.Children.OfType<Grid>().FirstOrDefault(grid => Grid.GetRow(grid) == 0);
            if (_diagnosticsHeader is not null)
            {
                var panels = _diagnosticsHeader.Children.OfType<StackPanel>().ToArray();
                _diagnosticsHeaderTitle = panels.FirstOrDefault();
                _diagnosticsHeaderActions = panels.Skip(1).FirstOrDefault();
            }

            _diagnosticsBody = DiagnosticsPage.Children.OfType<Grid>().FirstOrDefault(grid => Grid.GetRow(grid) == 4);
            if (_diagnosticsBody is not null)
            {
                var borders = _diagnosticsBody.Children.OfType<Border>().ToArray();
                _diagnosticsDoctorPanel = borders.FirstOrDefault();
                _diagnosticsHealthPanel = borders.Skip(1).FirstOrDefault();
            }
        }

        _dashboardCardsGrid ??= FindDescendants<Grid>(DashboardPage)
            .FirstOrDefault(grid => grid.ColumnDefinitions.Count == 3 && grid.Children.OfType<Border>().Count() == 3);
    }

    private void ApplyContentPadding(double width)
    {
        if (_contentGrid is null) return;
        _contentGrid.Padding = width switch
        {
            < 620 => new Thickness(10, 12, 10, 10),
            < 900 => new Thickness(16, 16, 16, 12),
            _ => new Thickness(24, 20, 24, 16)
        };
    }

    private void ApplyWidePageAlignment()
    {
        StretchScrollablePage(_dashboardContentPanel);
        StretchScrollablePage(_settingsContentPanel);
    }

    private static void StretchScrollablePage(StackPanel? panel)
    {
        if (panel is null) return;

        panel.MaxWidth = double.PositiveInfinity;
        panel.Width = double.NaN;
        panel.HorizontalAlignment = HorizontalAlignment.Stretch;

        var scrollViewer = FindAncestor<ScrollViewer>(panel);
        if (scrollViewer is not null)
        {
            scrollViewer.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        }
    }

    private void ApplyConnectionsLayout(double width, bool stacked)
    {
        ConfigureHeader(
            _connectionsHeader,
            _connectionsHeaderTitle,
            _connectionsHeaderActions,
            compact: width < 700);

        if (_connectionsSplitGrid is not null && _connectionsListPanel is not null && _connectionsDetailsPanel is not null)
        {
            _connectionsSplitGrid.ColumnDefinitions.Clear();
            _connectionsSplitGrid.RowDefinitions.Clear();

            if (stacked)
            {
                _connectionsSplitGrid.ColumnSpacing = 0;
                _connectionsSplitGrid.RowSpacing = 12;
                _connectionsSplitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                _connectionsSplitGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30, GridUnitType.Star) });
                _connectionsSplitGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(70, GridUnitType.Star) });

                Grid.SetColumn(_connectionsListPanel, 0);
                Grid.SetRow(_connectionsListPanel, 0);
                Grid.SetColumn(_connectionsDetailsPanel, 0);
                Grid.SetRow(_connectionsDetailsPanel, 1);
            }
            else
            {
                _connectionsSplitGrid.RowSpacing = 0;
                _connectionsSplitGrid.ColumnSpacing = 12;
                _connectionsSplitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30, GridUnitType.Star) });
                _connectionsSplitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70, GridUnitType.Star) });

                Grid.SetRow(_connectionsListPanel, 0);
                Grid.SetColumn(_connectionsListPanel, 0);
                Grid.SetRow(_connectionsDetailsPanel, 0);
                Grid.SetColumn(_connectionsDetailsPanel, 1);
            }
        }

        if (_connectionsPrimaryActions is not null)
        {
            _connectionsPrimaryActions.Orientation = width < 620 ? Orientation.Vertical : Orientation.Horizontal;
            _connectionsPrimaryActions.HorizontalAlignment = HorizontalAlignment.Left;
        }
    }

    private void ApplyLogsLayout(double width, bool veryNarrow)
    {
        ConfigureHeader(
            _logsHeader,
            _logsHeaderTitle,
            _logsHeaderActions,
            compact: width < 800);

        if (_logsFilterGrid is null || _logsFilterGrid.Children.Count < 5) return;

        var controls = _logsFilterGrid.Children.OfType<FrameworkElement>().Take(5).ToArray();
        _logsFilterGrid.RowDefinitions.Clear();
        _logsFilterGrid.ColumnDefinitions.Clear();

        if (width >= 900)
        {
            _logsFilterGrid.RowSpacing = 0;
            _logsFilterGrid.ColumnSpacing = 10;
            _logsFilterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30, GridUnitType.Star) });
            _logsFilterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28, GridUnitType.Star) });
            _logsFilterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16, GridUnitType.Star) });
            _logsFilterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(13, GridUnitType.Star) });
            _logsFilterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(13, GridUnitType.Star) });

            for (var index = 0; index < controls.Length; index++)
            {
                Grid.SetRow(controls[index], 0);
                Grid.SetColumn(controls[index], index);
                Grid.SetColumnSpan(controls[index], 1);
            }
            return;
        }

        _logsFilterGrid.ColumnSpacing = 10;
        _logsFilterGrid.RowSpacing = 8;
        if (veryNarrow)
        {
            _logsFilterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var index = 0; index < controls.Length; index++)
            {
                _logsFilterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(controls[index], index);
                Grid.SetColumn(controls[index], 0);
                Grid.SetColumnSpan(controls[index], 1);
            }
            return;
        }

        _logsFilterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _logsFilterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _logsFilterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _logsFilterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _logsFilterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(controls[0], 0);
        Grid.SetColumn(controls[0], 0);
        Grid.SetColumnSpan(controls[0], 2);
        Grid.SetRow(controls[1], 1);
        Grid.SetColumn(controls[1], 0);
        Grid.SetColumnSpan(controls[1], 1);
        Grid.SetRow(controls[2], 1);
        Grid.SetColumn(controls[2], 1);
        Grid.SetColumnSpan(controls[2], 1);
        Grid.SetRow(controls[3], 2);
        Grid.SetColumn(controls[3], 0);
        Grid.SetColumnSpan(controls[3], 1);
        Grid.SetRow(controls[4], 2);
        Grid.SetColumn(controls[4], 1);
        Grid.SetColumnSpan(controls[4], 1);
    }

    private void ApplyDiagnosticsLayout(double width)
    {
        ConfigureHeader(
            _diagnosticsHeader,
            _diagnosticsHeaderTitle,
            _diagnosticsHeaderActions,
            compact: width < 760);

        if (_diagnosticsBody is null || _diagnosticsDoctorPanel is null || _diagnosticsHealthPanel is null) return;

        _diagnosticsBody.RowDefinitions.Clear();
        _diagnosticsBody.ColumnDefinitions.Clear();

        if (width >= 820)
        {
            _diagnosticsBody.RowSpacing = 0;
            _diagnosticsBody.ColumnSpacing = 12;
            _diagnosticsBody.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50, GridUnitType.Star) });
            _diagnosticsBody.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50, GridUnitType.Star) });
            Grid.SetRow(_diagnosticsDoctorPanel, 0);
            Grid.SetColumn(_diagnosticsDoctorPanel, 0);
            Grid.SetRow(_diagnosticsHealthPanel, 0);
            Grid.SetColumn(_diagnosticsHealthPanel, 1);
            return;
        }

        _diagnosticsBody.ColumnSpacing = 0;
        _diagnosticsBody.RowSpacing = 12;
        _diagnosticsBody.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _diagnosticsBody.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50, GridUnitType.Star) });
        _diagnosticsBody.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50, GridUnitType.Star) });
        Grid.SetRow(_diagnosticsDoctorPanel, 0);
        Grid.SetColumn(_diagnosticsDoctorPanel, 0);
        Grid.SetRow(_diagnosticsHealthPanel, 1);
        Grid.SetColumn(_diagnosticsHealthPanel, 0);
    }

    private void ApplyDashboardLayout(double width)
    {
        if (_dashboardCardsGrid is null) return;
        var cards = _dashboardCardsGrid.Children.OfType<Border>().ToArray();
        if (cards.Length != 3) return;

        _dashboardCardsGrid.RowDefinitions.Clear();
        _dashboardCardsGrid.ColumnDefinitions.Clear();

        if (width >= 800)
        {
            _dashboardCardsGrid.RowSpacing = 0;
            _dashboardCardsGrid.ColumnSpacing = 12;
            for (var index = 0; index < 3; index++)
            {
                _dashboardCardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetRow(cards[index], 0);
                Grid.SetColumn(cards[index], index);
            }
            return;
        }

        _dashboardCardsGrid.ColumnSpacing = 0;
        _dashboardCardsGrid.RowSpacing = 12;
        for (var index = 0; index < 3; index++)
        {
            _dashboardCardsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(cards[index], index);
            Grid.SetColumn(cards[index], 0);
        }
        _dashboardCardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    }

    private void ApplySettingsLayout()
    {
        if (_settingsClientGrid is not null && _settingsClientGrid.ColumnDefinitions.Count == 3)
        {
            _settingsClientGrid.ColumnDefinitions[0].Width = new GridLength(28, GridUnitType.Star);
            _settingsClientGrid.ColumnDefinitions[1].Width = new GridLength(72, GridUnitType.Star);
            _settingsClientGrid.ColumnDefinitions[2].Width = GridLength.Auto;
        }

        if (_settingsRefreshGrid is not null && _settingsRefreshGrid.ColumnDefinitions.Count == 2)
        {
            _settingsRefreshGrid.ColumnDefinitions[0].Width = new GridLength(28, GridUnitType.Star);
            _settingsRefreshGrid.ColumnDefinitions[1].Width = new GridLength(72, GridUnitType.Star);
        }

        if (_settingsRefreshNumberBox is not null)
        {
            _settingsRefreshNumberBox.Width = double.NaN;
            _settingsRefreshNumberBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
    }

    private static void ConfigureHeader(
        Grid? header,
        StackPanel? titlePanel,
        StackPanel? actionPanel,
        bool compact)
    {
        if (header is null || titlePanel is null || actionPanel is null) return;

        header.RowDefinitions.Clear();
        header.ColumnDefinitions.Clear();

        if (!compact)
        {
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(titlePanel, 0);
            Grid.SetColumn(titlePanel, 0);
            Grid.SetRow(actionPanel, 0);
            Grid.SetColumn(actionPanel, 1);
            actionPanel.HorizontalAlignment = HorizontalAlignment.Right;
            actionPanel.Margin = new Thickness(0);
            return;
        }

        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(titlePanel, 0);
        Grid.SetColumn(titlePanel, 0);
        Grid.SetRow(actionPanel, 1);
        Grid.SetColumn(actionPanel, 0);
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