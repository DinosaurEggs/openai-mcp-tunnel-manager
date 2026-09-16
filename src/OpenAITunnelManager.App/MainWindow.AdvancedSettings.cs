using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace OpenAITunnelManager.App;

public sealed partial class MainWindow
{
    private bool _advancedSettingsUiConfigured;

    private void ConfigureAdvancedDirectoryPickers()
    {
        if (_advancedSettingsUiConfigured) return;

        // Configure the settings page directly from its known top-level XAML children.
        // Do not depend on the Expander content being materialized in the visual tree.
        var titlePanel = SettingsContentPanel.Children.OfType<StackPanel>().FirstOrDefault();
        var regularCard = SettingsContentPanel.Children.OfType<Border>().FirstOrDefault();
        var expander = SettingsContentPanel.Children.OfType<Expander>().FirstOrDefault();
        var footer = SettingsContentPanel.Children.OfType<Grid>().LastOrDefault();

        if (titlePanel is null || regularCard is null ||
            expander?.Content is not Border advancedCard ||
            advancedCard.Child is not StackPanel advancedPanel)
        {
            return;
        }

        var directoryFields = advancedPanel.Children.OfType<TextBox>().ToArray();
        if (directoryFields.Length < 2) return;

        _advancedSettingsUiConfigured = true;

        // Match the other pages: page title on the left, primary action on the right.
        var header = new Grid
        {
            ColumnSpacing = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        titlePanel.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(titlePanel, 0);
        header.Children.Add(titlePanel);

        var headerActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        var save = new Button
        {
            Content = "保存设置",
            VerticalAlignment = VerticalAlignment.Center
        };
        save.Click += SaveSettings_Click;
        headerActions.Children.Add(save);
        Grid.SetColumn(headerActions, 1);
        header.Children.Add(headerActions);

        var titleIndex = SettingsContentPanel.Children.IndexOf(titlePanel);
        SettingsContentPanel.Children.RemoveAt(titleIndex);
        SettingsContentPanel.Children.Insert(titleIndex, header);

        // The old footer only showed settings.json path and another save button.
        // The path is intentionally no longer shown in the settings page.
        if (footer is not null)
        {
            SettingsContentPanel.Children.Remove(footer);
        }

        var regularIndex = SettingsContentPanel.Children.IndexOf(regularCard);
        SettingsContentPanel.Children.Insert(regularIndex, CreateSettingsSectionTitle("常规设置"));

        // Advanced settings are always visible. Remove the Expander completely and
        // reuse its card as an ordinary section so opening it can never resize layout.
        var expanderIndex = SettingsContentPanel.Children.IndexOf(expander);
        expander.Content = null;
        SettingsContentPanel.Children.RemoveAt(expanderIndex);
        advancedCard.Margin = new Thickness(0);
        advancedCard.HorizontalAlignment = HorizontalAlignment.Stretch;
        advancedCard.MaxWidth = double.PositiveInfinity;
        SettingsContentPanel.Children.Insert(expanderIndex, CreateSettingsSectionTitle("高级设置"));
        SettingsContentPanel.Children.Insert(expanderIndex + 1, advancedCard);

        var description = advancedPanel.Children.OfType<TextBlock>().FirstOrDefault();
        if (description is not null)
        {
            description.Text = "可选：为 tunnel-client 子进程覆盖 Profile / State 目录。留空时继承 tunnel-client 当前环境或默认目录。";
            description.TextWrapping = TextWrapping.Wrap;
        }

        ConfigureDirectoryField(advancedPanel, directoryFields[0], isProfileDirectory: true);
        ConfigureDirectoryField(advancedPanel, directoryFields[1], isProfileDirectory: false);
    }

    private static TextBlock CreateSettingsSectionTitle(string text) => new()
    {
        Text = text,
        FontSize = 16,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Margin = new Thickness(0, 2, 0, -4)
    };

    private void ConfigureDirectoryField(StackPanel parent, TextBox textBox, bool isProfileDirectory)
    {
        var index = parent.Children.IndexOf(textBox);
        if (index < 0) return;
        parent.Children.RemoveAt(index);

        var label = new TextBlock
        {
            Text = isProfileDirectory ? "Profile 目录覆盖" : "State 目录覆盖",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        var environmentName = new TextBlock
        {
            Text = isProfileDirectory ? "TUNNEL_CLIENT_PROFILE_DIR" : "TUNNEL_CLIENT_STATE_DIR",
            FontSize = 12,
            Opacity = 0.65,
            TextWrapping = TextWrapping.Wrap
        };

        textBox.Header = null;
        textBox.IsReadOnly = true;
        textBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        textBox.VerticalAlignment = VerticalAlignment.Center;
        textBox.MinWidth = 0;
        textBox.Width = double.NaN;
        textBox.PlaceholderText = "未覆盖";

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
        field.Children.Add(environmentName);
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
