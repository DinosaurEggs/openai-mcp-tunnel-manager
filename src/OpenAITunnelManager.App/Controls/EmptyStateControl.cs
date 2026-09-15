using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace OpenAITunnelManager.App.Controls;

public sealed class EmptyStateControl : UserControl
{
    private readonly FontIcon _icon;
    private readonly TextBlock _title;
    private readonly TextBlock _description;
    private readonly Button _primaryButton;
    private readonly Button _secondaryButton;

    public EmptyStateControl()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _icon = new FontIcon
        {
            FontSize = 42,
            Opacity = 0.72,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _title = new TextBlock
        {
            FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _description = new TextBlock
        {
            Opacity = 0.72,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _primaryButton = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _secondaryButton = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _primaryButton.Click += PrimaryButton_Click;
        _secondaryButton.Click += SecondaryButton_Click;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 8
        };
        buttons.Children.Add(_primaryButton);
        buttons.Children.Add(_secondaryButton);

        var content = new StackPanel
        {
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(_icon);
        content.Children.Add(_title);
        content.Children.Add(_description);
        content.Children.Add(buttons);

        Grid.SetColumn(content, 1);
        Grid.SetRow(content, 1);
        root.Children.Add(content);
        Content = root;
    }

    public Func<Task>? PrimaryAction { get; set; }
    public Func<Task>? SecondaryAction { get; set; }

    public void Configure(
        string glyph,
        string title,
        string description,
        string? primaryText = null,
        string? secondaryText = null)
    {
        _icon.Glyph = glyph;
        _title.Text = title;
        _description.Text = description;

        _primaryButton.Content = primaryText ?? string.Empty;
        _primaryButton.Visibility = string.IsNullOrWhiteSpace(primaryText) ? Visibility.Collapsed : Visibility.Visible;
        _secondaryButton.Content = secondaryText ?? string.Empty;
        _secondaryButton.Visibility = string.IsNullOrWhiteSpace(secondaryText) ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        if (PrimaryAction is not null) await PrimaryAction();
    }

    private async void SecondaryButton_Click(object sender, RoutedEventArgs e)
    {
        if (SecondaryAction is not null) await SecondaryAction();
    }
}
