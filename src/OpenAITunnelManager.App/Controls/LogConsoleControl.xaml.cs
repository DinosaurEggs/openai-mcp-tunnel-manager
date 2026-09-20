using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenAITunnelManager.Core.Logging;
using WinUIEditor;

namespace OpenAITunnelManager.App.Controls;

public sealed partial class LogConsoleControl : UserControl, IDisposable
{
    private const int StyleDefault = 0;
    private const int StyleTrace = 1;
    private const int StyleDebug = 2;
    private const int StyleInfo = 3;
    private const int StyleWarn = 4;
    private const int StyleError = 5;
    private const int StyleFatal = 6;

    private bool _initialized;
    private bool _disposed;
    private bool _suppressBottomNotifications;

    public LogConsoleControl()
    {
        InitializeComponent();
        ActualThemeChanged += LogConsoleControl_ActualThemeChanged;
    }

    public event Action<bool>? BottomStateChanged;
    public event Action? Ready;

    public bool IsReady => _initialized;

    public void Append(IReadOnlyList<LogDisplayEntry> entries, bool followTail)
    {
        if (!_initialized || entries.Count == 0) return;

        var editor = EditorControl.Editor;
        var start = editor.Length;
        var builder = new StringBuilder();
        var spans = new List<(long Length, int Style)>(entries.Count);

        foreach (var item in entries)
        {
            var text = item.ToConsoleText() + Environment.NewLine;
            builder.Append(text);
            spans.Add((Encoding.UTF8.GetByteCount(text), StyleFor(item.Entry.Severity)));
        }

        var payload = builder.ToString();
        var byteLength = Encoding.UTF8.GetByteCount(payload);

        _suppressBottomNotifications = true;
        try
        {
            WithWritable(editor, () => editor.AppendText(byteLength, payload));
            ApplyStyles(editor, start, spans);
            if (followTail) ScrollToEndCore(editor);
        }
        finally
        {
            _suppressBottomNotifications = false;
            NotifyBottomState();
        }
    }

    public void ReplaceAll(IReadOnlyList<LogDisplayEntry> entries, bool followTail)
    {
        if (!_initialized) return;

        var builder = new StringBuilder();
        var spans = new List<(long Length, int Style)>(entries.Count);

        foreach (var item in entries)
        {
            var text = item.ToConsoleText() + Environment.NewLine;
            builder.Append(text);
            spans.Add((Encoding.UTF8.GetByteCount(text), StyleFor(item.Entry.Severity)));
        }

        _suppressBottomNotifications = true;
        try
        {
            var editor = EditorControl.Editor;
            WithWritable(editor, () => editor.SetText(builder.ToString()));
            ApplyStyles(editor, 0, spans);
            editor.EmptyUndoBuffer();
            if (followTail) ScrollToEndCore(editor);
        }
        finally
        {
            _suppressBottomNotifications = false;
            NotifyBottomState();
        }
    }

    public void Clear()
    {
        if (!_initialized) return;

        _suppressBottomNotifications = true;
        try
        {
            var editor = EditorControl.Editor;
            WithWritable(editor, editor.ClearAll);
            editor.EmptyUndoBuffer();
        }
        finally
        {
            _suppressBottomNotifications = false;
            NotifyBottomState();
        }
    }

    public void ScrollToEnd()
    {
        if (!_initialized) return;

        _suppressBottomNotifications = true;
        try
        {
            ScrollToEndCore(EditorControl.Editor);
        }
        finally
        {
            _suppressBottomNotifications = false;
            NotifyBottomState();
        }
    }

    public void SetWrap(bool wrap)
    {
        if (!_initialized) return;
        var editor = EditorControl.Editor;
        editor.WrapMode = wrap ? Wrap.Word : Wrap.None;
        editor.HScrollBar = !wrap;
        editor.ScrollWidthTracking = !wrap;
        NotifyBottomState();
    }

    public void FindNext(string? query, bool regex, bool caseSensitive)
    {
        Find(query, regex, caseSensitive, forward: true);
    }

    public void FindPrevious(string? query, bool regex, bool caseSensitive)
    {
        Find(query, regex, caseSensitive, forward: false);
    }

    public void Copy()
    {
        if (_initialized) EditorControl.Editor.Copy();
    }

    public void SelectAll()
    {
        if (_initialized) EditorControl.Editor.SelectAll();
    }

    private void Find(string? query, bool regex, bool caseSensitive, bool forward)
    {
        if (!_initialized || string.IsNullOrWhiteSpace(query)) return;

        var editor = EditorControl.Editor;
        var flags = FindOption.None;
        if (caseSensitive) flags |= FindOption.MatchCase;
        if (regex) flags |= FindOption.RegExp | FindOption.Cxx11RegEx;

        editor.SearchAnchor();
        var found = forward
            ? editor.SearchNext(flags, query)
            : editor.SearchPrev(flags, query);

        if (found < 0)
        {
            var edge = forward ? 0 : editor.Length;
            editor.CurrentPos = edge;
            editor.Anchor = edge;
            editor.SearchAnchor();
            found = forward
                ? editor.SearchNext(flags, query)
                : editor.SearchPrev(flags, query);
        }

        if (found >= 0) editor.ScrollCaret();
    }

    private void EditorControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized || _disposed) return;

        var editor = EditorControl.Editor;
        editor.UndoCollection = false;
        editor.ReadOnly = true;
        editor.TabWidth = 4;
        editor.MarginLeft = 8;
        editor.MarginRight = 8;
        editor.EndAtLastLine = true;
        editor.BufferedDraw = true;
        editor.WrapMode = Wrap.Word;
        editor.HScrollBar = false;
        editor.ScrollWidthTracking = false;
        editor.CaretWidth = 0;
        editor.UpdateUI += Editor_UpdateUI;

        ApplyTheme();
        _initialized = true;
        Ready?.Invoke();
        NotifyBottomState();
    }

    private void Editor_UpdateUI(Editor sender, UpdateUIEventArgs args)
    {
        if (!_suppressBottomNotifications) NotifyBottomState();
    }

    private void LogConsoleControl_ActualThemeChanged(FrameworkElement sender, object args)
    {
        if (_initialized) ApplyTheme();
    }

    private void ApplyTheme()
    {
        var editor = EditorControl.Editor;
        var dark = ActualTheme != ElementTheme.Light;
        var foreground = dark ? Rgb(214, 214, 214) : Rgb(36, 36, 36);
        var background = dark ? Rgb(30, 30, 30) : Rgb(250, 250, 250);

        editor.StyleSetFore(StyleDefault, foreground);
        editor.StyleSetBack(StyleDefault, background);
        editor.StyleSetFont(StyleDefault, "Cascadia Mono");
        editor.StyleSetSize(StyleDefault, 10);
        editor.StyleClearAll();

        for (var style = StyleTrace; style <= StyleFatal; style++)
        {
            editor.StyleSetBack(style, background);
            editor.StyleSetFont(style, "Cascadia Mono");
            editor.StyleSetSize(style, 10);
        }

        editor.StyleSetFore(StyleTrace, dark ? Rgb(128, 128, 128) : Rgb(96, 96, 96));
        editor.StyleSetFore(StyleDebug, dark ? Rgb(97, 175, 239) : Rgb(0, 95, 184));
        editor.StyleSetFore(StyleInfo, dark ? Rgb(181, 206, 168) : Rgb(16, 124, 16));
        editor.StyleSetFore(StyleWarn, dark ? Rgb(220, 220, 170) : Rgb(138, 100, 0));
        editor.StyleSetFore(StyleError, dark ? Rgb(244, 135, 113) : Rgb(196, 43, 28));
        editor.StyleSetFore(StyleFatal, dark ? Rgb(255, 99, 99) : Rgb(168, 0, 0));
        editor.StyleSetBold(StyleFatal, true);
    }

    private static void ApplyStyles(
        Editor editor,
        long start,
        IReadOnlyList<(long Length, int Style)> spans)
    {
        if (spans.Count == 0) return;
        editor.StartStyling(start, 0);
        foreach (var span in spans)
            editor.SetStyling(span.Length, span.Style);
    }

    private static void ScrollToEndCore(Editor editor)
    {
        editor.GotoPos(editor.Length);
        editor.ScrollCaret();
    }

    private bool IsAtBottom()
    {
        if (!_initialized) return true;

        var editor = EditorControl.Editor;
        if (editor.LineCount <= 1) return true;

        var lastDocLine = editor.LineCount - 1;
        var lastVisibleStart = editor.VisibleFromDocLine(lastDocLine);
        var lastVisibleEnd = lastVisibleStart + Math.Max(1, editor.WrapCount(lastDocLine)) - 1;
        var viewportEnd = editor.FirstVisibleLine + Math.Max(1, editor.LinesOnScreen);
        return viewportEnd >= lastVisibleEnd - 1;
    }

    private void NotifyBottomState()
    {
        BottomStateChanged?.Invoke(IsAtBottom());
    }

    private static int StyleFor(LogSeverity severity) => severity switch
    {
        LogSeverity.Trace => StyleTrace,
        LogSeverity.Debug => StyleDebug,
        LogSeverity.Info => StyleInfo,
        LogSeverity.Warn => StyleWarn,
        LogSeverity.Error => StyleError,
        LogSeverity.Fatal => StyleFatal,
        _ => StyleDefault
    };

    private static int Rgb(int red, int green, int blue) =>
        red | (green << 8) | (blue << 16);

    private static void WithWritable(Editor editor, Action action)
    {
        var wasReadOnly = editor.ReadOnly;
        if (wasReadOnly) editor.ReadOnly = false;
        try
        {
            action();
        }
        finally
        {
            if (wasReadOnly) editor.ReadOnly = true;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ActualThemeChanged -= LogConsoleControl_ActualThemeChanged;
        if (_initialized)
            EditorControl.Editor.UpdateUI -= Editor_UpdateUI;
        BottomStateChanged = null;
        Ready = null;
    }
}
