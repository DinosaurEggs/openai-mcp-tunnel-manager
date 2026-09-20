using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;

namespace OpenAITunnelManager.App.Controls;

public sealed partial class LogConsoleControl : UserControl, IDisposable
{
    private const string VirtualHost = "logconsole.local";
    private readonly Queue<string> _pendingMessages = new();
    private bool _ready;
    private bool _disposed;

    public LogConsoleControl()
    {
        InitializeComponent();
        Loaded += LogConsoleControl_Loaded;
    }

    public event Action<bool>? BottomStateChanged;
    public event Action<string>? LinkInvoked;
    public event Action? Ready;

    public bool IsReady => _ready;

    public Task AppendAsync(IEnumerable<string> lines, bool followTail) =>
        PostAsync(new { type = "append", lines = lines.ToArray(), followTail });

    public Task ReplaceAllAsync(IEnumerable<string> lines, bool followTail) =>
        PostAsync(new { type = "replaceAll", lines = lines.ToArray(), followTail });

    public Task ClearAsync() => PostAsync(new { type = "clear" });
    public Task ScrollToBottomAsync() => PostAsync(new { type = "scrollToBottom" });
    public Task FindNextAsync() => PostAsync(new { type = "findNext" });
    public Task FindPreviousAsync() => PostAsync(new { type = "findPrevious" });
    public Task CopyAsync() => PostAsync(new { type = "copy" });
    public Task SelectAllAsync() => PostAsync(new { type = "selectAll" });
    public Task FocusConsoleAsync() => PostAsync(new { type = "focus" });

    public Task SetSearchAsync(string? query, bool regex, bool caseSensitive) =>
        PostAsync(new
        {
            type = "search",
            query = query?.Trim() ?? string.Empty,
            regex,
            caseSensitive
        });

    public Task SetWrapAsync(bool enabled) =>
        PostAsync(new { type = "setWrap", enabled });

    public Task SetThemeAsync(ElementTheme theme) =>
        PostAsync(new { type = "setTheme", mode = theme == ElementTheme.Light ? "light" : "dark" });

    private async void LogConsoleControl_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= LogConsoleControl_Loaded;
        try
        {
            await InitializeAsync();
        }
        catch
        {
            // MainWindow keeps the original log path visible and reports refresh failures.
        }
    }

    private async Task InitializeAsync()
    {
        if (_disposed || Browser.CoreWebView2 is not null) return;

        await Browser.EnsureCoreWebView2Async();
        if (_disposed || Browser.CoreWebView2 is null) return;

        var assets = Path.Combine(AppContext.BaseDirectory, "Assets", "LogConsole", "dist");
        if (!Directory.Exists(assets))
            throw new DirectoryNotFoundException($"日志控制台资源不存在：{assets}");

        Browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        Browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
        Browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
        Browser.CoreWebView2.SetVirtualHostNameToFolderMapping(
            VirtualHost,
            assets,
            CoreWebView2HostResourceAccessKind.DenyCors);

        Browser.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
        Browser.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
        Browser.Source = new Uri($"https://{VirtualHost}/index.html");
    }

    private void CoreWebView2_NavigationStarting(
        CoreWebView2 sender,
        CoreWebView2NavigationStartingEventArgs args)
    {
        if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Host, VirtualHost, StringComparison.OrdinalIgnoreCase))
        {
            args.Cancel = true;
        }
    }

    private void CoreWebView2_WebMessageReceived(
        CoreWebView2 sender,
        CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            using var document = JsonDocument.Parse(args.WebMessageAsJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var typeValue) ||
                typeValue.ValueKind != JsonValueKind.String)
            {
                return;
            }

            switch (typeValue.GetString())
            {
                case "ready":
                    _ready = true;
                    FlushPending();
                    Ready?.Invoke();
                    break;
                case "viewport":
                    if (root.TryGetProperty("atBottom", out var bottom) &&
                        bottom.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        BottomStateChanged?.Invoke(bottom.GetBoolean());
                    }
                    break;
                case "openLink":
                    if (root.TryGetProperty("url", out var url) &&
                        url.ValueKind == JsonValueKind.String &&
                        url.GetString() is { } text)
                    {
                        LinkInvoked?.Invoke(text);
                    }
                    break;
                case "copyText":
                    if (root.TryGetProperty("text", out var copyText) &&
                        copyText.ValueKind == JsonValueKind.String)
                    {
                        var package = new DataPackage();
                        package.SetText(copyText.GetString() ?? string.Empty);
                        Clipboard.SetContent(package);
                    }
                    break;
            }
        }
        catch (JsonException)
        {
        }
    }

    private Task PostAsync(object payload)
    {
        if (_disposed) return Task.CompletedTask;
        var json = JsonSerializer.Serialize(payload);
        if (!_ready || Browser.CoreWebView2 is null)
        {
            _pendingMessages.Enqueue(json);
            return Task.CompletedTask;
        }

        Browser.CoreWebView2.PostWebMessageAsJson(json);
        return Task.CompletedTask;
    }

    private void FlushPending()
    {
        if (!_ready || Browser.CoreWebView2 is null) return;
        while (_pendingMessages.Count > 0)
            Browser.CoreWebView2.PostWebMessageAsJson(_pendingMessages.Dequeue());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Loaded -= LogConsoleControl_Loaded;
        _pendingMessages.Clear();
        if (Browser.CoreWebView2 is not null)
        {
            Browser.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
            Browser.CoreWebView2.NavigationStarting -= CoreWebView2_NavigationStarting;
        }
        Browser.Close();
    }
}
