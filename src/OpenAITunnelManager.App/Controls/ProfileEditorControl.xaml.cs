using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenAITunnelManager.Core.Models;
using WinUIEditor;

namespace OpenAITunnelManager.App.Controls;

public sealed partial class ProfileEditorControl : UserControl
{
    private bool _initializing;
    private bool _advancedEdited;
    private bool _createMode;
    private string _originalTargetKind = "server_url";

    public ProfileEditorControl()
    {
        // XAML properties such as ComboBox.SelectedIndex can raise SelectionChanged while
        // InitializeComponent is still constructing later named controls. Suppress all editor
        // event handlers until the complete visual tree exists.
        _initializing = true;
        try
        {
            InitializeComponent();
        }
        finally
        {
            _initializing = false;
        }

        RawEditor.Editor.Modified += RawEditor_Modified;
    }

    public string ProfileName => ProfileNameBox.Text.Trim();
    public string TunnelId => TunnelIdBox.Text.Trim();
    public string TargetValue => TargetBox.Text.Trim();
    public Dictionary<string, string> StdioEnvironment => ParseStdioEnvironment(StdioEnvironmentBox.Text);
    public string RawText => RawEditor.Editor.GetText(RawEditor.Editor.Length + 1);
    public string Secret => SecretBox.Password;
    public bool Enabled => EnabledCheckBox.IsChecked == true;
    public bool AutoConnect => AutoConnectCheckBox.IsChecked == true;
    public bool AutoReconnect => AutoReconnectCheckBox.IsChecked == true;
    public bool DeleteSecret => DeleteSecretCheckBox.IsChecked == true;
    public bool AdvancedEdited => _advancedEdited;
    public bool CommonTargetSupported => _originalTargetKind is "server_url" or "command";
    public McpType McpType => McpTypeCombo.SelectedIndex == 1 ? McpType.Stdio : McpType.Http;
    public string ExpectedTargetKind => McpType == McpType.Stdio ? "command" : "server_url";

    public void InitializeForCreate()
    {
        _initializing = true;
        _createMode = true;
        _advancedEdited = false;
        _originalTargetKind = "server_url";

        ProfileNameBox.Text = string.Empty;
        ProfileNameBox.IsReadOnly = false;
        TunnelIdBox.Text = string.Empty;
        McpTypeCombo.IsEnabled = true;
        McpTypeCombo.Visibility = Visibility.Visible;
        McpTypeCombo.SelectedIndex = 0;
        TargetBox.Header = "main MCP URL";
        TargetBox.PlaceholderText = "例如 http://127.0.0.1:8000/mcp";
        TargetBox.IsEnabled = true;
        TargetBox.Text = string.Empty;
        TargetHelpText.Visibility = Visibility.Collapsed;
        StdioEnvironmentBox.Text = string.Empty;
        UpdateStdioEnvironmentVisibility();
        EnabledCheckBox.IsChecked = true;
        AutoConnectCheckBox.IsChecked = false;
        AutoReconnectCheckBox.IsChecked = false;
        CredentialStatusText.Text = "未保存 Runtime API Key";
        SecretBox.PlaceholderText = "可留空；未保存时使用环境变量";
        DeleteSecretCheckBox.Visibility = Visibility.Collapsed;
        DeleteSecretCheckBox.IsChecked = false;
        AdvancedHelpText.Text = "高级配置会随“基本”页自动生成；手动修改后将保留你的完整 YAML / JSON。";
        EditorTabs.SelectedIndex = 0;
        ClearValidationError();

        _initializing = false;
        SyncCreateRawFromBasic();
    }

    public void InitializeForEdit(
        string name,
        string tunnelId,
        string targetKind,
        string targetValue,
        IReadOnlyDictionary<string, string>? stdioEnvironment,
        bool enabled,
        bool autoConnect,
        bool autoReconnect,
        bool hasSavedSecret,
        string rawText)
    {
        _initializing = true;
        _createMode = false;
        _advancedEdited = false;
        _originalTargetKind = targetKind;

        ProfileNameBox.Text = name;
        ProfileNameBox.IsReadOnly = true;
        TunnelIdBox.Text = tunnelId;
        McpTypeCombo.IsEnabled = false;
        McpTypeCombo.Visibility = CommonTargetSupported ? Visibility.Visible : Visibility.Collapsed;
        McpTypeCombo.SelectedIndex = targetKind == "command" ? 1 : 0;
        TargetBox.Text = targetValue;
        TargetBox.IsEnabled = CommonTargetSupported;
        TargetBox.Header = targetKind == "command"
            ? "main MCP Command"
            : CommonTargetSupported ? "main MCP URL" : $"main MCP target ({targetKind})";
        TargetBox.PlaceholderText = targetKind == "command" ? "例如 dotnet mcp-server.dll" : "例如 http://127.0.0.1:8000/mcp";
        TargetHelpText.Text = CommonTargetSupported
            ? "MCP 类型来自现有 Profile；如需切换 HTTP / STDIO，请在高级配置中修改完整结构。"
            : "该 MCP target 不是常用格式，请在“高级配置”中直接修改。";
        TargetHelpText.Visibility = Visibility.Visible;
        StdioEnvironmentBox.Text = FormatStdioEnvironment(stdioEnvironment);
        UpdateStdioEnvironmentVisibility();

        EnabledCheckBox.IsChecked = enabled;
        AutoConnectCheckBox.IsChecked = autoConnect;
        AutoReconnectCheckBox.IsChecked = autoReconnect;
        CredentialStatusText.Text = hasSavedSecret ? "已保存 Runtime API Key" : "未保存 Runtime API Key";
        SecretBox.PlaceholderText = hasSavedSecret ? "留空表示保持现有凭据" : "留空使用环境变量";
        DeleteSecretCheckBox.Visibility = hasSavedSecret ? Visibility.Visible : Visibility.Collapsed;
        DeleteSecretCheckBox.IsChecked = false;
        AdvancedHelpText.Text = CommonTargetSupported
            ? "保存时会将“基本”页中的 Tunnel ID 和 main MCP target 同步到此配置。"
            : "当前 Profile 使用非常用 MCP 结构；main MCP target 请直接在这里修改。";
        SetRawEditorText(rawText);
        EditorTabs.SelectedIndex = 0;
        ClearValidationError();

        _initializing = false;
    }

    public void ShowValidationError(string message, bool advanced = false)
    {
        ValidationInfo.Title = "无法保存";
        ValidationInfo.Message = message;
        ValidationInfo.IsOpen = true;
        EditorTabs.SelectedIndex = advanced ? 1 : 0;
    }

    public void ClearValidationError()
    {
        ValidationInfo.IsOpen = false;
        ValidationInfo.Title = string.Empty;
        ValidationInfo.Message = string.Empty;
    }

    private void EditorTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        if (_createMode && EditorTabs.SelectedIndex == 1 && !_advancedEdited)
        {
            SyncCreateRawFromBasic();
        }
    }

    private void McpTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        TargetBox.Header = McpType == McpType.Stdio ? "main MCP Command" : "main MCP URL";
        TargetBox.PlaceholderText = McpType == McpType.Stdio ? "例如 dotnet mcp-server.dll" : "例如 http://127.0.0.1:8000/mcp";
        UpdateStdioEnvironmentVisibility();
        if (_createMode && !_advancedEdited) SyncCreateRawFromBasic();
    }

    private void BasicField_Changed(object sender, TextChangedEventArgs e)
    {
        if (_initializing || !_createMode || _advancedEdited) return;
        SyncCreateRawFromBasic();
    }

    private void RawEditor_Modified(Editor sender, ModifiedEventArgs e)
    {
        if (_initializing) return;
        var type = (ModificationFlags)e.ModificationType;
        if ((type & (ModificationFlags.InsertText | ModificationFlags.DeleteText)) == ModificationFlags.None) return;
        _advancedEdited = true;
    }

    private void RawEditor_Loaded(object sender, RoutedEventArgs e)
    {
        var editor = RawEditor.Editor;
        editor.WrapMode = Wrap.None;
        editor.HScrollBar = true;
        editor.ScrollWidthTracking = true;
        editor.TabWidth = 2;
        editor.UseTabs = false;
    }

    private void SetRawEditorText(string text)
    {
        RawEditor.HighlightingLanguage = LooksLikeJson(text) ? "json" : "yaml";
        RawEditor.Editor.SetText(text);
        RawEditor.Editor.EmptyUndoBuffer();
    }

    private static bool LooksLikeJson(string value)
    {
        var trimmed = value.AsSpan().TrimStart();
        return !trimmed.IsEmpty && trimmed[0] is '{' or '[';
    }

    private void UpdateStdioEnvironmentVisibility()
    {
        StdioEnvironmentPanel.Visibility =
            McpType == McpType.Stdio && (_createMode || CommonTargetSupported)
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    public static Dictionary<string, string> ParseStdioEnvironment(string? text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text)) return result;

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) continue;

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                throw new ArgumentException($"STDIO 环境变量第 {index + 1} 行必须使用 KEY=VALUE 格式");
            }

            var name = line[..separator].Trim();
            var value = line[(separator + 1)..];
            if (name.Length == 0 || name.Contains('\0'))
            {
                throw new ArgumentException($"STDIO 环境变量第 {index + 1} 行的名称无效");
            }
            if (value.Contains('\0'))
            {
                throw new ArgumentException($"STDIO 环境变量 {name} 的值包含无效字符");
            }
            if (!result.TryAdd(name, value))
            {
                throw new ArgumentException($"STDIO 环境变量 {name} 重复定义");
            }
        }

        return result;
    }

    public static string FormatStdioEnvironment(IReadOnlyDictionary<string, string>? environment)
    {
        if (environment is null || environment.Count == 0) return string.Empty;
        return string.Join(
            Environment.NewLine,
            environment.Select(static pair => $"{pair.Key}={pair.Value}"));
    }

    private void SyncCreateRawFromBasic()
    {
        _initializing = true;
        try
        {
            var tunnelId = JsonSerializer.Serialize(TunnelId);
            var keyRef = JsonSerializer.Serialize("env:CONTROL_PLANE_API_KEY");
            var target = JsonSerializer.Serialize(TargetValue);
            var targetSection = McpType == McpType.Http
                ? $"  server_urls:{Environment.NewLine}    - channel: main{Environment.NewLine}      url: {target}{Environment.NewLine}"
                : $"  commands:{Environment.NewLine}    - channel: main{Environment.NewLine}      command: {target}{Environment.NewLine}";
            SetRawEditorText(
                $"config_version: 1{Environment.NewLine}" +
                $"control_plane:{Environment.NewLine}" +
                $"  tunnel_id: {tunnelId}{Environment.NewLine}" +
                $"  api_key: {keyRef}{Environment.NewLine}" +
                $"mcp:{Environment.NewLine}" +
                targetSection);
        }
        finally
        {
            _initializing = false;
        }
    }
}
