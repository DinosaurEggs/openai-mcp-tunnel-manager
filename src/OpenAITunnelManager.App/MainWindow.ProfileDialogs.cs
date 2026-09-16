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

        // Primary layout follows the current window proportionally. Small component spacing,
        // padding and control metrics remain numeric inside the editor itself.
        var dialogWidth = windowWidth * 0.66;
        var dialogMaxHeight = windowHeight * 0.88;
        var contentWidth = dialogWidth * 0.94;
        var contentMaxHeight = windowHeight * 0.70;

        editor.Width = double.NaN;
        editor.Height = double.NaN;
        editor.MinWidth = 0;
        editor.MinHeight = 0;
        editor.MaxWidth = double.PositiveInfinity;
        editor.MaxHeight = contentMaxHeight;
        editor.HorizontalAlignment = HorizontalAlignment.Stretch;
        editor.VerticalAlignment = VerticalAlignment.Top;

        // Do not give the host a fixed height. The basic form should occupy only its natural
        // height so the credential section does not leave a large blank area underneath. When
        // the window is smaller than the form, the editor MaxHeight constrains the body and the
        // page's own ScrollViewer handles the overflow.
        var host = new Grid
        {
            Width = contentWidth,
            MinWidth = 0,
            MinHeight = 0,
            MaxHeight = contentMaxHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top
        };
        host.Children.Add(editor);

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = title,
            Content = host,
            PrimaryButtonText = primaryText,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 0,
            MaxWidth = double.PositiveInfinity,
            MaxHeight = dialogMaxHeight
        };

        dialog.Resources["ContentDialogMinWidth"] = dialogWidth;
        dialog.Resources["ContentDialogMaxWidth"] = dialogWidth;
        dialog.Resources["ContentDialogMaxHeight"] = dialogMaxHeight;
        dialog.Opened += (_, _) => editor.ResetInitialViewport();

        return dialog;
    }
}
