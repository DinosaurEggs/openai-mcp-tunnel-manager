using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using OpenAITunnelManager.Core.Logging;
using Windows.UI;
using Windows.UI.Text;

namespace OpenAITunnelManager.App.Controls;

public sealed partial class AnsiLogLineControl : UserControl
{
    private const int MaxBrushCacheEntries = 1024;
    private static readonly Dictionary<uint, SolidColorBrush> BrushCache = [];
    private bool _isWrapped;

    public static readonly DependencyProperty LineProperty = DependencyProperty.Register(
        nameof(Line),
        typeof(LogLine),
        typeof(AnsiLogLineControl),
        new PropertyMetadata(null, OnLineChanged));

    public AnsiLogLineControl()
    {
        InitializeComponent();
        IsWrapped = DefaultIsWrapped;
    }

    public static bool DefaultIsWrapped { get; set; }

    public LogLine? Line
    {
        get => (LogLine?)GetValue(LineProperty);
        set => SetValue(LineProperty, value);
    }

    public bool IsWrapped
    {
        get => _isWrapped;
        set
        {
            if (_isWrapped == value) return;
            _isWrapped = value;
            Presenter.TextWrapping = value ? TextWrapping.Wrap : TextWrapping.NoWrap;
        }
    }

    private static void OnLineChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is AnsiLogLineControl control)
        {
            control.Render(args.NewValue as LogLine);
        }
    }

    private void Render(LogLine? line)
    {
        Presenter.Text = string.Empty;
        Presenter.Inlines.Clear();
        Presenter.TextHighlighters.Clear();

        if (line is null || string.IsNullOrEmpty(line.Text)) return;
        if (line.Spans is not { Count: > 0 })
        {
            Presenter.Text = line.Text;
            return;
        }

        var position = 0;
        foreach (var span in line.Spans.OrderBy(static span => span.Start))
        {
            var start = Math.Clamp(span.Start, 0, line.Text.Length);
            var end = Math.Clamp(span.Start + span.Length, start, line.Text.Length);
            if (start > position)
            {
                Presenter.Inlines.Add(new Run { Text = line.Text[position..start] });
            }

            if (end > start)
            {
                var run = new Run { Text = line.Text[start..end] };
                if (span.Style.Foreground is { } foreground)
                {
                    run.Foreground = ResolveBrush(foreground, span.Style.Dim);
                }
                if (span.Style.Bold) run.FontWeight = FontWeights.Bold;
                if (span.Style.Italic) run.FontStyle = FontStyle.Italic;
                if (span.Style.Underline) run.TextDecorations = TextDecorations.Underline;
                Presenter.Inlines.Add(run);

                if (span.Style.Background is { } background)
                {
                    var highlighter = new TextHighlighter
                    {
                        Background = ResolveBrush(background, dim: false)
                    };
                    highlighter.Ranges.Add(new TextRange { StartIndex = start, Length = end - start });
                    Presenter.TextHighlighters.Add(highlighter);
                }
            }
            position = Math.Max(position, end);
        }

        if (position < line.Text.Length)
        {
            Presenter.Inlines.Add(new Run { Text = line.Text[position..] });
        }
    }

    private static SolidColorBrush ResolveBrush(AnsiColor ansiColor, bool dim)
    {
        var color = ResolveColor(ansiColor, dim);
        var key = ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
        if (BrushCache.TryGetValue(key, out var cached)) return cached;
        if (BrushCache.Count >= MaxBrushCacheEntries) BrushCache.Clear();
        var brush = new SolidColorBrush(color);
        BrushCache[key] = brush;
        return brush;
    }

    private static Color ResolveColor(AnsiColor color, bool dim)
    {
        byte red;
        byte green;
        byte blue;
        if (color.Kind == AnsiColorKind.Rgb)
        {
            red = color.R;
            green = color.G;
            blue = color.B;
        }
        else
        {
            (red, green, blue) = ResolveIndexedColor(color.Index);
        }
        return Color.FromArgb(dim ? (byte)0xA0 : (byte)0xFF, red, green, blue);
    }

    private static (byte Red, byte Green, byte Blue) ResolveIndexedColor(byte index)
    {
        ReadOnlySpan<uint> standard =
        [
            0x0C0C0C, 0xC50F1F, 0x13A10E, 0xC19C00,
            0x0037DA, 0x881798, 0x3A96DD, 0xCCCCCC,
            0x767676, 0xE74856, 0x16C60C, 0xF9F1A5,
            0x3B78FF, 0xB4009E, 0x61D6D6, 0xF2F2F2
        ];

        if (index < 16)
        {
            var value = standard[index];
            return ((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        }

        if (index < 232)
        {
            var value = index - 16;
            var r = value / 36;
            var g = value % 36 / 6;
            var b = value % 6;
            static byte Component(int component) => component == 0 ? (byte)0 : (byte)(55 + component * 40);
            return (Component(r), Component(g), Component(b));
        }

        var gray = (byte)(8 + (index - 232) * 10);
        return (gray, gray, gray);
    }
}
