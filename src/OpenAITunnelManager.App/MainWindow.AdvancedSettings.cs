using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;

namespace OpenAITunnelManager.App;

public sealed partial class MainWindow
{
    private bool _advancedSettingsUiConfigured;

    private void ConfigureAdvancedDirectoryPickers()
    {
        if (_advancedSettingsUiConfigured) return;

        var expander = FindDescendants<Expander>(SettingsPage)
            .FirstOrDefault(item => string.Equals(item.Header?.ToString(), "高级：tunnel-client 目录覆盖", StringComparison.Ordinal));
        if (expander is null) return;

        var profileTextBox = FindDescendants<TextBox>(expander)
            .FirstOrDefault(item => string.Equals(item.Header?.ToString(), "TUNNEL_CLIENT_PROFILE_DIR", StringComparison.Ordinal));
        var stateTextBox = FindDescendants<TextBox>(expander)
            .FirstOrDefault(item => string.Equals(item.Header?.ToString(), "TUNNEL_CLIENT_STATE_DIR", StringComparison.Ordinal));
        if (profileTextBox is null || stateTextBox is null) return;

        _advancedSettingsUiConfigured = true;

        expander.HorizontalAlignment = HorizontalAlignment.Stretch;
        expander.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        expander.Width = double.NaN;
        expander.MaxWidth = double.PositiveInfinity;

        if (expander.Content is Border border)
        {
            border.HorizontalAlignment = HorizontalAlignment.Stretch;
            border.Width = double.NaN;
            border.MaxWidth = double.PositiveInfinity;
        }

        ConfigureDirectoryField(profileTextBox, isProfileDirectory: true);
        ConfigureDirectoryField(stateTextBox, isProfileDirectory: false);
    }

    private void ConfigureDirectoryField(TextBox textBox, bool isProfileDirectory)
    {
        if (VisualTreeHelper.GetParent(textBox) is not StackPanel parent) return;

        var index = -1;
        for (var i = 0; i < parent.Children.Count; i++)
        {
            if (!ReferenceEquals(parent.Children[i], textBox)) continue;
            index = i;
            break;
        }
        if (index < 0) return;

        parent.Children.RemoveAt(index);

        var label = new TextBlock
        {
            Text = isProfileDirectory
                ? "Profile 目录覆盖（TUNNEL_CLIENT_PROFILE_DIR）"
                : "State 目录覆盖（TUNNEL_CLIENT_STATE_DIR）",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };

        textBox.Header = null;
        textBox.IsReadOnly = true;
        textBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        textBox.VerticalAlignment = VerticalAlignment.Center;
        textBox.MinWidth = 0;
        textBox.Width = double.NaN;
        textBox.PlaceholderText = "未覆盖：继承 tunnel-client 当前环境 / 默认目录";

        var browse = new Button
        {
            Content = "选择目录",
            VerticalAlignment = VerticalAlignment.Center,
            Tag = isProfileDirectory ? "profile" : "state"
        };
        browse.Click += BrowseDirectoryOverride_Click;

        var clear = new Button
        {
            Content = "清除",
            VerticalAlignment = VerticalAlignment.Center,
            Tag = isProfileDirectory ? "profile" : "state"
        };
        clear.Click += ClearDirectoryOverride_Click;

        var row = new Grid
        {
            ColumnSpacing = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 0
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(textBox, 0);
        Grid.SetColumn(browse, 1);
        Grid.SetColumn(clear, 2);
        row.Children.Add(textBox);
        row.Children.Add(browse);
        row.Children.Add(clear);

        var field = new StackPanel
        {
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        field.Children.Add(label);
        field.Children.Add(row);
        parent.Children.Insert(index, field);
    }

    private async void BrowseDirectoryOverride_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string kind }) return;

        try
        {
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
            var folder = await picker.PickSingleFolderAsync();
            if (folder is null) return;

            if (string.Equals(kind, "profile", StringComparison.Ordinal))
            {
                ViewModel.ProfileDirectoryOverride = folder.Path;
            }
            else
            {
                ViewModel.StateDirectoryOverride = folder.Path;
            }
        }
        catch (Exception exception)
        {
            await ShowErrorAsync($"选择目录失败：{exception.Message}");
        }
    }

    private void ClearDirectoryOverride_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string kind }) return;
        if (string.Equals(kind, "profile", StringComparison.Ordinal))
        {
            ViewModel.ProfileDirectoryOverride = string.Empty;
        }
        else
        {
            ViewModel.StateDirectoryOverride = string.Empty;
        }
    }
}
