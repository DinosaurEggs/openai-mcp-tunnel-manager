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

        if (RootGrid.ActualHeight > 1)
        {
            viewer.MaxHeight = RootGrid.ActualHeight * 0.72;
        }

        return viewer;
    }
}
