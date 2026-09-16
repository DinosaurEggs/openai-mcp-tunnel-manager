namespace OpenAITunnelManager.App.Controls;

public sealed partial class ProfileEditorControl
{
    public void ResetInitialViewport()
    {
        // Editing an existing Profile changes which controls are focusable. WinUI may then scroll
        // the first tab while ContentDialog is opening to bring that control into view, hiding the
        // section title and Profile-name header. Reset once after the dialog is fully opened.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (EditorTabs.SelectedIndex != 0) return;
            BasicScrollViewer.ChangeView(null, 0d, null, disableAnimation: true);
        });
    }
}
