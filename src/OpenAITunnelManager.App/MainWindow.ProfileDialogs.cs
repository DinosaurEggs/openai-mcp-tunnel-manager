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

        // The host owns the editor size. Do not lock both ContentDialog and UserControl widths:
        // WinUI's ContentDialog template has its own presenter constraints and double-locking the
        // dimensions causes off-center placement and focus-time re-measurement.
        var desiredDialogWidth = Math.Clamp(windowWidth * 0.62, 600d, 860d);
        var dialogWidth = Math.Min(desiredDialogWidth, Math.Max(360d, windowWidth - 64d));
        var desiredContentHeight = Math.Clamp(windowHeight * 0.68, 500d, 720d);
        var availableContentHeight = Math.Max(260d, windowHeight * 0.94 - 180d);
        var contentHeight = Math.Min(desiredContentHeight, availableContentHeight);
        var contentWidth = Math.Max(0d, dialogWidth - 48d);

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
            MaxHeight = windowHeight * 0.94
        };

        // WinUI ships ContentDialog with a comparatively narrow theme maximum. Override the
        // presenter resources for this editor only so the popup is measured around the host and
        // centered consistently instead of silently clamping to the platform default width.
        dialog.Resources["ContentDialogMinWidth"] = dialogWidth;
        dialog.Resources["ContentDialogMaxWidth"] = dialogWidth;
        dialog.Opened += (_, _) => editor.ResetInitialViewport();

        return dialog;
    }
}
