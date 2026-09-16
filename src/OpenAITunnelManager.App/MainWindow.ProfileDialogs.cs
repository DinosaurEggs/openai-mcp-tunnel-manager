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

        // Width is owned only by the dialog host. The editor itself stretches inside it.
        var desiredDialogWidth = Math.Clamp(windowWidth * 0.62, 600d, 860d);
        var dialogWidth = Math.Min(desiredDialogWidth, Math.Max(360d, windowWidth - 64d));
        var contentWidth = Math.Max(0d, dialogWidth - 48d);

        // ContentDialog still needs vertical room for its title area, template padding and
        // fixed footer buttons. Previously the body itself was capped at 720 epx, which made
        // both create/edit dialogs unnecessarily short on large windows and clipped the
        // credential section against the footer. Allocate the body from the actual window
        // height instead and reserve chrome space explicitly.
        var dialogMaxHeight = Math.Max(480d, windowHeight * 0.92);
        var chromeReserve = Math.Clamp(windowHeight * 0.18, 160d, 220d);
        var contentHeight = Math.Max(320d, dialogMaxHeight - chromeReserve);

        editor.Width = double.NaN;
        editor.Height = double.NaN;
        editor.MinWidth = 0;
        editor.MinHeight = 0;
        editor.MaxWidth = double.PositiveInfinity;
        editor.MaxHeight = double.PositiveInfinity;
        editor.HorizontalAlignment = HorizontalAlignment.Stretch;
        editor.VerticalAlignment = VerticalAlignment.Stretch;

        var host = new Grid
        {
            Width = contentWidth,
            Height = contentHeight,
            MinWidth = 0,
            MinHeight = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
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

        // Override the template limits for this editor only. The body remains internally
        // scrollable, while the title and footer always stay inside the dialog bounds.
        dialog.Resources["ContentDialogMinWidth"] = dialogWidth;
        dialog.Resources["ContentDialogMaxWidth"] = dialogWidth;
        dialog.Resources["ContentDialogMaxHeight"] = dialogMaxHeight;
        dialog.Opened += (_, _) => editor.ResetInitialViewport();

        return dialog;
    }
}
