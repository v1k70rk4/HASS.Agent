namespace HASS.Agent.Companion.Logging;

internal sealed class FileLog : IDisposable
{
    private readonly string _path;
    private readonly object _gate = new();
    private bool _disposed;

    public FileLog(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public void Info(string message)
    {
        Write("INFO", message);
    }

    public void Warning(string message)
    {
        Write("WARN", message);
    }

    public void Error(Exception exception, string message)
    {
        Write("ERROR", $"{message}{Environment.NewLine}{exception}");
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private void Write(string level, string message)
    {
        if (_disposed)
        {
            return;
        }

        var line = $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}";
        lock (_gate)
        {
            File.AppendAllText(_path, line);
        }
    }
}
