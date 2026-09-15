using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace OpenAITunnelManager.App;

public sealed partial class MainWindow
{
    private object PrepareDialogContent(object content)
    {
        if (content is not FrameworkElement element) return content;

        element.HorizontalAlignment = HorizontalAlignment.Stretch;
        element.MinWidth = 0;

        var viewer = content as ScrollViewer ?? new ScrollViewer
        {
            Content = element,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };

        viewer.HorizontalAlignment = HorizontalAlignment.Stretch;
        viewer.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        viewer.MinWidth = 0;

        // Dialog content follows the current window instead of using fixed pixel heights.
        // ContentDialog still applies its own platform maximums; this only ensures a large
        // editor becomes scrollable before it can push buttons/title out of view.
        if (RootGrid.ActualHeight > 1)
        {
            viewer.MaxHeight = RootGrid.ActualHeight * 0.72;
        }

        return viewer;
    }

    private void ConfigureProfileTextEditor(TextBox editor)
    {
        editor.HorizontalAlignment = HorizontalAlignment.Stretch;
        editor.MinWidth = 0;
        editor.Width = double.NaN;
        editor.Height = double.NaN;
        if (RootGrid.ActualHeight > 1)
        {
            editor.MaxHeight = RootGrid.ActualHeight * 0.42;
        }
    }
}
