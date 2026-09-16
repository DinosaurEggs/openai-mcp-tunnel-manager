using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenAITunnelManager.App.Controls;

namespace OpenAITunnelManager.App;

public sealed partial class MainWindow
{
    private ContentDialog NewProfileEditorDialog(string title, ProfileEditorControl editor, string primaryText)
    {
        var windowWidth = RootGrid.ActualWidth > 1 ? RootGrid.ActualWidth : 1000d;
        var windowHeight = RootGrid.ActualHeight > 1 ? RootGrid.ActualHeight : 800d;

        // Freeze the dialog width for its lifetime. TextBox focus visuals, clear buttons and
        // scroll bar visibility can no longer cause ContentDialog to re-measure narrower.
        var dialogWidth = Math.Clamp(windowWidth * 0.62, 560d, 800d);
        var contentWidth = Math.Max(480d, dialogWidth - 48d);
        var contentHeight = Math.Clamp(windowHeight * 0.62, 460d, 660d);

        editor.Width = contentWidth;
        editor.Height = contentHeight;
        editor.MinWidth = contentWidth;
        editor.MaxWidth = contentWidth;
        editor.HorizontalAlignment = HorizontalAlignment.Stretch;
        editor.VerticalAlignment = VerticalAlignment.Stretch;

        return new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = title,
            Content = editor,
            PrimaryButtonText = primaryText,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            Width = dialogWidth,
            MinWidth = dialogWidth,
            MaxWidth = dialogWidth,
            MaxHeight = windowHeight * 0.92
        };
    }
}
