using System.Text;

namespace OpenAITunnelManager.Core.Logging;

public sealed class LogConsoleBuffer
{
    public const int DefaultMaxEntries = 30_000;
    public const int DefaultMaxCharacters = 8 * 1024 * 1024;

    private readonly Queue<StructuredLogEntry> _entries = new();
    private readonly int _maxEntries;
    private readonly int _maxBytes;
    private int _byteCount;
    public int RawByteCount => _byteCount;

    public LogConsoleBuffer(
        int maxEntries = DefaultMaxEntries,
        int maxCharacters = DefaultMaxCharacters)
    {
        _maxEntries = Math.Max(100, maxEntries);
        _maxBytes = Math.Max(64 * 1024, maxCharacters);
    }

    public int Count => _entries.Count;
    public bool WasTrimmed { get; private set; }

    public void Clear()
    {
        _entries.Clear();
        _byteCount = 0;
        WasTrimmed = false;
    }

    public bool Append(IEnumerable<StructuredLogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        foreach (var entry in entries)
        {
            _entries.Enqueue(entry);
            _byteCount += Encoding.UTF8.GetByteCount(entry.RawText);
        }
        return Trim();
    }

    public IReadOnlyList<StructuredLogEntry> Snapshot(string? level = null, string? component = null)
    {
        if ((string.IsNullOrWhiteSpace(level) ||
            string.Equals(level, "全部", StringComparison.OrdinalIgnoreCase)) && string.IsNullOrWhiteSpace(component))
        {
            return _entries.ToArray();
        }

        return _entries.Where(entry => MatchesLevel(entry.Severity, level) && MatchesComponent(entry, component)).ToArray();
    }

    public IReadOnlyList<LogDisplayEntry> SnapshotDisplay(
        string? level = null,
        bool foldDuplicates = false, string? component = null)
    {
        var source = Snapshot(level, component);
        if (!foldDuplicates || source.Count == 0)
            return source.Select(static entry => new LogDisplayEntry(entry, 1)).ToArray();

        var result = new List<LogDisplayEntry>(source.Count);
        StructuredLogEntry? current = null;
        var repeat = 0;
        foreach (var entry in source)
        {
            if (current is not null &&
                string.Equals(current.DuplicateKey, entry.DuplicateKey, StringComparison.Ordinal))
            {
                repeat++;
                continue;
            }

            if (current is not null)
                result.Add(new LogDisplayEntry(current, repeat));

            current = entry;
            repeat = 1;
        }

        if (current is not null)
            result.Add(new LogDisplayEntry(current, repeat));

        return result;
    }

    public static bool MatchesComponent(StructuredLogEntry entry, string? component) =>
        string.IsNullOrWhiteSpace(component) || entry.Logger.Contains(component.Trim(), StringComparison.OrdinalIgnoreCase);

    public static bool MatchesLevel(LogSeverity severity, string? level) => level?.Trim().ToUpperInvariant() switch
    {
        "TRACE" => severity == LogSeverity.Trace,
        "DEBUG" => severity == LogSeverity.Debug,
        "INFO" => severity == LogSeverity.Info,
        "WARN" => severity == LogSeverity.Warn,
        "ERROR" => severity is LogSeverity.Error or LogSeverity.Fatal,
        _ => true
    };

    private bool Trim()
    {
        var trimmed = false;
        while (_entries.Count > 0 &&
               (_entries.Count > _maxEntries || _byteCount > _maxBytes))
        {
            _byteCount -= Encoding.UTF8.GetByteCount(_entries.Dequeue().RawText);
            WasTrimmed = true;
            trimmed = true;
        }
        return trimmed;
    }
}
