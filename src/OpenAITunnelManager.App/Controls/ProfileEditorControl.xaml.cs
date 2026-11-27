using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenAITunnelManager.Core.Models;
using OpenAITunnelManager.Infrastructure.TunnelClient;
using WinUIEditor;

namespace OpenAITunnelManager.App.Controls;

public sealed partial class ProfileEditorControl : UserControl
{
    private bool _initializing;
    private bool _advancedEdited;
    private bool _createMode;
    private string _originalTargetKind = "server_url";
    private int _previousTab;
    private bool _switchingTab;
    private readonly List<(string Section, string Key, bool Boolean, TextBox Box)> _officialFields = [];
    private readonly Dictionary<(string, string), string> _fieldBaseline = new();

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
        BuildOfficialFields();
    }

    public string ProfileName => ProfileNameBox.Text.Trim();
    public string TunnelId => TunnelIdBox.Text.Trim();
    public string TargetValue => TargetBox.Text.Trim();
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
        LoadOfficialFields();
    }

    public void InitializeForEdit(
        string name,
        string tunnelId,
        string targetKind,
        string targetValue,
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
        LoadOfficialFields();
        EditorTabs.SelectedIndex = 0;
        ClearValidationError();

        _initializing = false;
    }

    public void ShowValidationError(string message, bool advanced = false)
    {
        ValidationInfo.Title = "无法保存";
        ValidationInfo.Message = message;
        ValidationInfo.IsOpen = true;
        if (advanced) EditorTabs.SelectedIndex = 1;
    }

    public void ClearValidationError()
    {
        ValidationInfo.IsOpen = false;
        ValidationInfo.Title = string.Empty;
        ValidationInfo.Message = string.Empty;
    }

    public string PrepareDocument()
    {
        ClearValidationError();
        if (EditorTabs.SelectedIndex == 0) ApplyBasicToDocument();
        return RawText;
    }

    private void ApplyBasicToDocument()
    {
        if (_createMode && !_advancedEdited) SyncCreateRawFromBasic();
        var metadata = ProfileDocumentEditor.ReadMetadata(RawText);
        var updated = ProfileDocumentEditor.ApplyCommonFields(
            RawText, metadata.TunnelId, metadata.TargetKind, metadata.TargetValue,
            TunnelId, CommonTargetSupported ? TargetValue : metadata.TargetValue);
        foreach (var field in _officialFields)
        {
            var value = field.Box.Text.Trim();
            if (value == _fieldBaseline.GetValueOrDefault((field.Section, field.Key), string.Empty)) continue;
            if (field.Key == "api_key" && value.Length > 0 &&
                !value.StartsWith("env:", StringComparison.Ordinal) && !value.StartsWith("file:", StringComparison.Ordinal) && !value.StartsWith('$'))
                throw new InvalidOperationException("API Key 只能填写环境变量或文件引用，密钥请使用凭据栏");
            updated = ProfileDocumentEditor.SetSectionScalar(updated, field.Section, field.Key, value.Length == 0 ? null : value, field.Boolean);
        }
        if (updated == RawText) return;
        _initializing = true;
        try
        {
            SetRawEditorText(updated);
            _advancedEdited = true;
            McpTypeCombo.IsEnabled = false;
            LoadOfficialFields();
        }
        finally { _initializing = false; }
    }

    private void LoadBasicFromDocument()
    {
        var metadata = ProfileDocumentEditor.ReadMetadata(RawText);
        _initializing = true;
        try
        {
            _originalTargetKind = metadata.TargetKind;
            TunnelIdBox.Text = metadata.TunnelId;
            TargetBox.Text = metadata.TargetValue;
            TargetBox.IsEnabled = CommonTargetSupported;
            McpTypeCombo.Visibility = CommonTargetSupported ? Visibility.Visible : Visibility.Collapsed;
            McpTypeCombo.IsEnabled = _createMode && !_advancedEdited;
            McpTypeCombo.SelectedIndex = metadata.TargetKind == "command" ? 1 : 0;
            TargetBox.Header = metadata.TargetKind == "command" ? "main MCP Command" : "main MCP URL";
        }
        finally { _initializing = false; }
    }

    private void EditorTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || _switchingTab) return;
        var selected = EditorTabs.SelectedIndex;
        try
        {
            if (_previousTab == 0 && selected == 1) ApplyBasicToDocument();
            else if (_previousTab == 1 && selected == 0) LoadBasicFromDocument();
            _previousTab = selected;
            ClearValidationError();
        }
        catch (Exception exception)
        {
            _switchingTab = true;
            try { EditorTabs.SelectedIndex = _previousTab; }
            finally { _switchingTab = false; }
            ShowValidationError(exception.Message);
        }
    }

    private void McpTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;
        TargetBox.Header = McpType == McpType.Stdio ? "main MCP Command" : "main MCP URL";
        TargetBox.PlaceholderText = McpType == McpType.Stdio ? "例如 dotnet mcp-server.dll" : "例如 http://127.0.0.1:8000/mcp";
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
    public void LoadTemplate(string text)
    {
        _initializing = true;
        try { SetRawEditorText(text); _advancedEdited = true; }
        finally { _initializing = false; }
        LoadBasicFromDocument();
    }

    private void BuildOfficialFields()
    {
        void Add(string section, string key, string label, bool boolean = false)
        {
            var box = new TextBox { Header = label, HorizontalAlignment = HorizontalAlignment.Stretch };
            _officialFields.Add((section, key, boolean, box));
            OfficialFieldsPanel.Children.Add(box);
        }
        Add("control_plane", "residency", "Residency（global / us / eu 等）");
        Add("control_plane", "base_url", "Control plane Base URL");
        Add("control_plane", "url_path", "Control plane URL Path");
        Add("control_plane", "api_key", "Runtime API Key 引用（env: / file:）");
        Add("control_plane", "aws_identity_provider_id", "AWS Identity Provider ID");
        Add("control_plane", "aws_service_account_id", "AWS Service Account ID");
        Add("control_plane", "client_cert", "mTLS 证书引用");
        Add("control_plane", "client_key", "mTLS 私钥引用");
        Add("mcp", "oauth_discovery", "OAuth discovery（auto / disabled）");
        Add("mcp", "stdio_failure_policy", "STDIO failure policy（restart / exit）");
        Add("mcp", "forward_trace_context", "转发 Trace Context（true / false）", true);
        Add("tracing", "enabled", "本地 Trace（true / false）", true);
        Add("tracing", "file", "Trace JSONL 文件");
        Add("admin_ui", "base_path", "Admin UI Base Path");
        Add("admin_ui", "public_origin", "Admin UI Public Origin");
    }

    private void LoadOfficialFields()
    {
        foreach (var field in _officialFields)
        {
            var value = ProfileDocumentEditor.ReadSectionScalar(RawText, field.Section, field.Key) ?? string.Empty;
            if (field.Key == "api_key" && !value.StartsWith("env:", StringComparison.Ordinal) &&
                !value.StartsWith("file:", StringComparison.Ordinal) && !value.StartsWith('$')) value = string.Empty;
            field.Box.Text = value;
            _fieldBaseline[(field.Section, field.Key)] = value;
        }
    }
}
