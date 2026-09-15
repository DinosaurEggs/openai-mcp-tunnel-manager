using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace OpenAITunnelManager.App;

public sealed partial class MainWindow
{
    private bool _responsiveLayoutEnabled;
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

        var narrow = contentWidth < 820;
        var medium = contentWidth < 1080;
        var veryNarrow = contentWidth < 620;

        ApplyContentPadding(contentWidth);
        ApplyConnectionsLayout(contentWidth, medium, narrow);
        ApplyLogsLayout(contentWidth, veryNarrow);
        ApplyDiagnosticsLayout(contentWidth);
        ApplyDashboardLayout(contentWidth);

        if (RootGrid.ActualWidth < 1080)
        {
            Navigation.IsPaneOpen = false;
        }
    }

    private void CacheResponsiveElements()
    {
        _contentGrid ??= VisualTreeHelper.GetParent(ConnectionsPage) as Grid;

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

    private void ApplyConnectionsLayout(double width, bool medium, bool narrow)
    {
        ConfigureHeader(
            _connectionsHeader,
            _connectionsHeaderTitle,
            _connectionsHeaderActions,
            compact: width < 700);

        if (_connectionsSplitGrid is not null && _connectionsListPanel is not null && _connectionsDetailsPanel is not null)
        {
            if (medium)
            {
                _connectionsSplitGrid.ColumnDefinitions.Clear();
                _connectionsSplitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                _connectionsSplitGrid.RowDefinitions.Clear();
                _connectionsSplitGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(narrow ? 190 : 220) });
                _connectionsSplitGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
                _connectionsSplitGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                Grid.SetColumn(_connectionsListPanel, 0);
                Grid.SetRow(_connectionsListPanel, 0);
                Grid.SetColumn(_connectionsDetailsPanel, 0);
                Grid.SetRow(_connectionsDetailsPanel, 2);
            }
            else
            {
                _connectionsSplitGrid.RowDefinitions.Clear();
                _connectionsSplitGrid.ColumnDefinitions.Clear();
                _connectionsSplitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
                _connectionsSplitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
                _connectionsSplitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                Grid.SetRow(_connectionsListPanel, 0);
                Grid.SetColumn(_connectionsListPanel, 0);
                Grid.SetRow(_connectionsDetailsPanel, 0);
                Grid.SetColumn(_connectionsDetailsPanel, 2);
            }
        }

        if (_connectionsPrimaryActions is not null)
        {
            _connectionsPrimaryActions.Orientation = width < 680 ? Orientation.Vertical : Orientation.Horizontal;
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

        var controls = _logsFilterGrid.Children.Cast<UIElement>().Take(5).ToArray();
        _logsFilterGrid.RowDefinitions.Clear();
        _logsFilterGrid.ColumnDefinitions.Clear();

        if (width >= 900)
        {
            _logsFilterGrid.RowSpacing = 0;
            _logsFilterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
            _logsFilterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            _logsFilterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            _logsFilterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _logsFilterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _logsFilterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (var index = 0; index < controls.Length; index++)
            {
                Grid.SetRow(controls[index], 0);
                Grid.SetColumn(controls[index], index);
                Grid.SetColumnSpan(controls[index], 1);
            }
            return;
        }

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
            _diagnosticsBody.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _diagnosticsBody.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(_diagnosticsDoctorPanel, 0);
            Grid.SetColumn(_diagnosticsDoctorPanel, 0);
            Grid.SetRow(_diagnosticsHealthPanel, 0);
            Grid.SetColumn(_diagnosticsHealthPanel, 1);
            return;
        }

        _diagnosticsBody.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _diagnosticsBody.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _diagnosticsBody.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
        _diagnosticsBody.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_diagnosticsDoctorPanel, 0);
        Grid.SetColumn(_diagnosticsDoctorPanel, 0);
        Grid.SetRow(_diagnosticsHealthPanel, 2);
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
            for (var index = 0; index < 3; index++)
            {
                _dashboardCardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetRow(cards[index], 0);
                Grid.SetColumn(cards[index], index);
            }
            return;
        }

        for (var index = 0; index < 3; index++)
        {
            _dashboardCardsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(cards[index], index);
            Grid.SetColumn(cards[index], 0);
        }
        _dashboardCardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _dashboardCardsGrid.RowSpacing = 12;
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