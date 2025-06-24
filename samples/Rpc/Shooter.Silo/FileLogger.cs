using System.Collections.Concurrent;

namespace Shooter.Silo;

public class FileLoggerProvider : ILoggerProvider
{
    private readonly string _filePath;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private readonly StreamWriter _writer;
    private readonly object _lock = new();

    public FileLoggerProvider(string filePath)
    {
        _filePath = filePath;
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        _writer = new StreamWriter(filePath, append: true) { AutoFlush = true };
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new FileLogger(name, _writer, _lock));
    }

    public void Dispose()
    {
        _loggers.Clear();
        _writer?.Dispose();
    }
}

public class FileLogger : ILogger
{
    private readonly string _categoryName;
    private readonly StreamWriter _writer;
    private readonly object _lock;

    public FileLogger(string categoryName, StreamWriter writer, object lockObject)
    {
        _categoryName = categoryName;
        _writer = writer;
        _lock = lockObject;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => null!;

    public bool IsEnabled(LogLevel logLevel) => true; // Let the logging framework handle filtering based on configuration

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        lock (_lock)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var message = formatter(state, exception);
            _writer.WriteLine($"{timestamp} [{logLevel,-5}] {_categoryName}: {message}");
            
            if (exception != null)
            {
                _writer.WriteLine($"Exception: {exception}");
            }
        }
    }
}