using System.Diagnostics;
using System.Text;

namespace OpenAITunnelManager.Infrastructure.TunnelClient;

internal sealed class RollingForegroundLog : IDisposable
{
    private const long MaxBytes = 16L * 1024 * 1024;
    private const int History = 3;
    private readonly string _path;
    private readonly object _gate = new();
    private StreamWriter? _writer;
    private bool _disposed;

    public RollingForegroundLog(string path)
    {
        _path = path;
        _writer = Open();
    }

    public void WriteLine(string line)
    {
        lock (_gate)
        {
            if (_disposed) return;
            try
            {
                _writer ??= Open();
                if (_writer.BaseStream.Length >= MaxBytes)
                {
                    _writer.Dispose();
                    _writer = null;
                    for (var i = History; i > 1; i--)
                        if (File.Exists(_path + "." + (i - 1)))
                            File.Move(_path + "." + (i - 1), _path + "." + i, true);
                    File.Move(_path, _path + ".1", true);
                    _writer = Open();
                }
                _writer.WriteLine(line);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Foreground log write failed: {exception.Message}");
            }
        }
    }

    private StreamWriter Open() => new(new FileStream(_path, FileMode.Append, FileAccess.Write,
        FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan), new UTF8Encoding(false)) { AutoFlush = true };

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }
}
