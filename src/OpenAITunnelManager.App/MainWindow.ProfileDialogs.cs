using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OpenAITunnelManager.App.Controls;
using Windows.System;
using Windows.UI;

namespace OpenAITunnelManager.App;

public sealed partial class MainWindow
{
    private Grid? _profileModalOverlay;
    private Border? _profileModalCard;
    private TextBlock? _profileModalTitle;
    private ContentControl? _profileModalContent;
    private Button? _profileModalPrimaryButton;
    private Button? _profileModalCancelButton;
    private ProfileEditorControl? _activeProfileEditor;
    private Func<bool>? _profileModalValidate;
    private TaskCompletionSource<bool>? _profileModalCompletion;

    private Task<bool> ShowProfileEditorDialogAsync(
        string title,
        ProfileEditorControl editor,
        string primaryText,
        Func<bool> validate)
    {
        if (_profileModalCompletion is not null)
        {
            throw new InvalidOperationException("Profile 编辑弹窗已经打开。");
        }

        EnsureProfileModalHost();

        _activeProfileEditor = editor;
        _profileModalValidate = validate;
        _profileModalCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        editor.Width = double.NaN;
        editor.Height = double.NaN;
        editor.MinWidth = 0;
        editor.MinHeight = 0;
        editor.MaxWidth = double.PositiveInfinity;
        editor.MaxHeight = double.PositiveInfinity;
        editor.HorizontalAlignment = HorizontalAlignment.Stretch;
        editor.VerticalAlignment = VerticalAlignment.Stretch;

        _profileModalTitle!.Text = title;
        _profileModalPrimaryButton!.Content = primaryText;
        _profileModalContent!.Content = editor;
        UpdateProfileModalTheme();
        ApplyProfileModalLayout();

        RootGrid.SizeChanged += RootGrid_ProfileModalSizeChanged;
        _profileModalOverlay!.Visibility = Visibility.Visible;

        DispatcherQueue.TryEnqueue(editor.ResetInitialViewport);
        return _profileModalCompletion.Task;
    }

    private void EnsureProfileModalHost()
    {
        if (_profileModalOverlay is not null) return;

        var overlay = new Grid
        {
            Visibility = Visibility.Collapsed,
            Background = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsTabStop = true
        };
        Grid.SetRow(overlay, 0);
        Grid.SetRowSpan(overlay, 2);
        Canvas.SetZIndex(overlay, 10_000);
        overlay.KeyDown += ProfileModalOverlay_KeyDown;

        var cardGrid = new Grid
        {
            Padding = new Thickness(22, 18, 22, 16)
        };
        cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            FontSize = 20,
            FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 },
            Margin = new Thickness(0, 0, 0, 16),
            TextWrapping = TextWrapping.Wrap
        };
        cardGrid.Children.Add(title);

        var content = new ContentControl
        {
            MinWidth = 0,
            MinHeight = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        Grid.SetRow(content, 1);
        cardGrid.Children.Add(content);

        var footer = new Grid
        {
            Margin = new Thickness(0, 18, 0, 0),
            ColumnSpacing = 10
        };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var primaryButton = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        primaryButton.Click += ProfileModalPrimaryButton_Click;
        footer.Children.Add(primaryButton);

        var cancelButton = new Button
        {
            Content = "取消",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        Grid.SetColumn(cancelButton, 1);
        cancelButton.Click += ProfileModalCancelButton_Click;
        footer.Children.Add(cancelButton);

        Grid.SetRow(footer, 2);
        cardGrid.Children.Add(footer);

        var card = new Border
        {
            Child = cardGrid,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        overlay.Children.Add(card);
        RootGrid.Children.Add(overlay);

        _profileModalOverlay = overlay;
        _profileModalCard = card;
        _profileModalTitle = title;
        _profileModalContent = content;
        _profileModalPrimaryButton = primaryButton;
        _profileModalCancelButton = cancelButton;
    }

    private void ApplyProfileModalLayout()
    {
        if (_profileModalCard is null || _profileModalContent is null || _activeProfileEditor is null) return;

        var availableWidth = Math.Max(1d, RootGrid.ActualWidth);
        var availableHeight = Math.Max(1d, RootGrid.ActualHeight);

        // The modal frame has a stable percentage-based size. Switching between the basic
        // and advanced tabs cannot resize it; only a window resize changes these dimensions.
        _profileModalCard.Width = availableWidth * 0.66;
        _profileModalCard.Height = availableHeight * 0.82;
        _profileModalContent.Width = double.NaN;
        _profileModalContent.Height = double.NaN;
        _profileModalContent.MaxHeight = double.PositiveInfinity;
        _activeProfileEditor.Width = double.NaN;
        _activeProfileEditor.Height = double.NaN;
        _activeProfileEditor.MaxHeight = double.PositiveInfinity;
    }

    private void UpdateProfileModalTheme()
    {
        if (_profileModalCard is null) return;

        var dark = RootGrid.ActualTheme == ElementTheme.Dark;
        _profileModalCard.Background = new SolidColorBrush(dark
            ? Color.FromArgb(0xFF, 0x20, 0x20, 0x20)
            : Color.FromArgb(0xFF, 0xFA, 0xFA, 0xFA));
        _profileModalCard.BorderBrush = new SolidColorBrush(dark
            ? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x24, 0x00, 0x00, 0x00));
    }

    private void RootGrid_ProfileModalSizeChanged(object sender, SizeChangedEventArgs e) => ApplyProfileModalLayout();

    private void ProfileModalPrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_profileModalValidate is null || !_profileModalValidate()) return;
        CloseProfileModal(true);
    }

    private void ProfileModalCancelButton_Click(object sender, RoutedEventArgs e) => CloseProfileModal(false);

    private void ProfileModalOverlay_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        e.Handled = true;
        CloseProfileModal(false);
    }

    private void CloseProfileModal(bool accepted)
    {
        if (_profileModalCompletion is null) return;

        RootGrid.SizeChanged -= RootGrid_ProfileModalSizeChanged;
        _profileModalOverlay!.Visibility = Visibility.Collapsed;
        _profileModalContent!.Content = null;

        _activeProfileEditor = null;
        _profileModalValidate = null;
        var completion = _profileModalCompletion;
        _profileModalCompletion = null;
        completion.TrySetResult(accepted);
    }
}
