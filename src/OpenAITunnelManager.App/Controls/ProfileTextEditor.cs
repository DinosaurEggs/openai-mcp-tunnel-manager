using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace OpenAITunnelManager.App.Controls;

public sealed class ProfileTextEditor : TextBox
{
    private readonly InputCursor _arrowCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
    private readonly InputCursor _textCursor = InputSystemCursor.Create(InputSystemCursorShape.IBeam);

    public ProfileTextEditor()
    {
        ProtectedCursor = _textCursor;
        AddHandler(PointerMovedEvent, new PointerEventHandler(OnEditorPointerMoved), handledEventsToo: true);
    }

    private void OnEditorPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        ProtectedCursor = IsInsideScrollBar(e.OriginalSource as DependencyObject)
            ? _arrowCursor
            : _textCursor;
    }

    private static bool IsInsideScrollBar(DependencyObject? source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is ScrollBar) return true;
        }

        return false;
    }
}
