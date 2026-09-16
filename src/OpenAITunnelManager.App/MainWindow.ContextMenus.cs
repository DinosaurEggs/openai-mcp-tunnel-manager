using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace OpenAITunnelManager.App;

public sealed partial class MainWindow
{
    private bool _itemContextMenuConfigured;

    private void ConfigureItemOnlyContextMenu()
    {
        if (_itemContextMenuConfigured) return;
        _itemContextMenuConfigured = true;

        // A ContextFlyout on ListView itself also opens when the user right-clicks the
        // empty surface. Configuration commands are target-specific, so only create the
        // flyout after a ListViewItem has actually been hit.
        ConnectionsList.ContextFlyout = null;
        ConnectionsList.RightTapped -= ConnectionsList_RightTapped;
        ConnectionsList.RightTapped += ConnectionsList_ItemOnlyRightTapped;
    }

    private void ConnectionsList_ItemOnlyRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        if (e.OriginalSource is not DependencyObject source) return;

        var container = FindAncestor<ListViewItem>(source);
        if (container is null) return;

        ConnectionsList.SelectedItem = container.Content;

        var flyout = new MenuFlyout();
        var edit = new MenuFlyoutItem
        {
            Text = "编辑",
            IsEnabled = ViewModel.CanEditSelected
        };
        edit.Click += EditProfile_Click;

        var delete = new MenuFlyoutItem
        {
            Text = "删除",
            IsEnabled = ViewModel.CanDeleteSelected
        };
        delete.Click += DeleteSelected_Click;

        flyout.Items.Add(edit);
        flyout.Items.Add(delete);

        // Anchor the flyout to the actual right-click position rather than the whole row.
        // ShowAt(container) lets WinUI choose an edge of the item and makes the menu appear
        // detached from the pointer, especially on wide list rows.
        flyout.ShowAt(
            ConnectionsList,
            new FlyoutShowOptions
            {
                Position = e.GetPosition(ConnectionsList)
            });
    }
}
