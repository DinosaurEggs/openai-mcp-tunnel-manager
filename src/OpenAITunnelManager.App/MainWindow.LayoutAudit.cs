using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OpenAITunnelManager.App;

public sealed partial class MainWindow
{
    private bool _supplementalLayoutConfigured;

    private void ConfigureSupplementalLayout()
    {
        if (_supplementalLayoutConfigured) return;
        _supplementalLayoutConfigured = true;
        RootGrid.SizeChanged += SupplementalLayout_SizeChanged;
        RootGrid.Loaded += SupplementalLayout_Loaded;
        RequestSupplementalLayout();
    }

    private void SupplementalLayout_Loaded(object sender, RoutedEventArgs e) => RequestSupplementalLayout();
    private void SupplementalLayout_SizeChanged(object sender, SizeChangedEventArgs e) => RequestSupplementalLayout();

    private void RequestSupplementalLayout() =>
        DispatcherQueue.TryEnqueue(ApplySupplementalLayout);

    private void ApplySupplementalLayout()
    {
        var width = ContentGrid.ActualWidth;
        if (width <= 1) width = Math.Max(0, RootGrid.ActualWidth - 64);
        if (width <= 1) return;

        var narrow = width < 650;

        // The primary connection toolbar only needs vertical stacking in the genuinely narrow
        // one-column layout. Stacking it in compact mode wastes a large part of the detail pane.
        ConnectionsPrimaryActions.Orientation = narrow ? Orientation.Vertical : Orientation.Horizontal;
        ConnectionsPrimaryActions.HorizontalAlignment = narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        foreach (var button in ConnectionsPrimaryActions.Children.OfType<Button>())
        {
            button.HorizontalAlignment = narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        }

        ConfigureSettingsHeader(narrow);
        ConfigureDirectoryOverrideRows(narrow);
    }

    private void ConfigureSettingsHeader(bool narrow)
    {
        var header = SettingsContentPanel.Children.OfType<Grid>().FirstOrDefault();
        if (header is null || header.Children.Count < 2) return;

        var title = header.Children.OfType<StackPanel>().FirstOrDefault();
        var saveButton = header.Children.OfType<Button>().FirstOrDefault();
        if (title is null || saveButton is null) return;

        header.RowDefinitions.Clear();
        header.ColumnDefinitions.Clear();

        if (narrow)
        {
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(title, 0);
            Grid.SetColumn(title, 0);
            Grid.SetRow(saveButton, 1);
            Grid.SetColumn(saveButton, 0);
            saveButton.HorizontalAlignment = HorizontalAlignment.Left;
            saveButton.Margin = new Thickness(0, 10, 0, 0);
            return;
        }

        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(title, 0);
        Grid.SetColumn(title, 0);
        Grid.SetRow(saveButton, 0);
        Grid.SetColumn(saveButton, 1);
        saveButton.HorizontalAlignment = HorizontalAlignment.Right;
        saveButton.Margin = new Thickness(0);
    }

    private void ConfigureDirectoryOverrideRows(bool narrow)
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

                Grid.SetRow(textBox, 0);
                Grid.SetColumn(textBox, 0);
                Grid.SetColumnSpan(textBox, 2);
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
            Grid.SetRow(textBox, 0);
            Grid.SetColumn(textBox, 0);
            Grid.SetColumnSpan(textBox, 1);
            for (var index = 0; index < buttons.Length; index++)
            {
                Grid.SetRow(buttons[index], 0);
                Grid.SetColumn(buttons[index], index + 1);
                Grid.SetColumnSpan(buttons[index], 1);
                buttons[index].HorizontalAlignment = HorizontalAlignment.Left;
            }
        }
    }
}
