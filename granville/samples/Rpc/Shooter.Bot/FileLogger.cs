using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Shooter.Bot;

public class ConsoleRedirector : TextWriter
{
    private readonly TextWriter _originalWriter;
    private readonly StreamWriter _fileWriter;
    private readonly string _prefix;
    private readonly object _lock;

    public ConsoleRedirector(TextWriter originalWriter, StreamWriter fileWriter, string prefix, object lockObject)
    {
        _originalWriter = originalWriter;
        _fileWriter = fileWriter;
        _prefix = prefix;
        _lock = lockObject;
    }

    public override Encoding Encoding => _originalWriter.Encoding;

    public override void Write(char value)
    {
        lock (_lock)
        {
            _originalWriter.Write(value);
            _fileWriter.Write(value);
        }
    }

    public override void Write(string? value)
    {
        if (value == null) return;
        
        lock (_lock)
        {
            _originalWriter.Write(value);
            _fileWriter.Write(value);
        }
    }

    public override void WriteLine(string? value)
    {
        lock (_lock)
        {
            _originalWriter.WriteLine(value);
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            _fileWriter.WriteLine($"{timestamp} [{_prefix}] {value ?? string.Empty}");
        }
    }

    public override void WriteLine()
    {
        lock (_lock)
        {
            _originalWriter.WriteLine();
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            _fileWriter.WriteLine($"{timestamp} [{_prefix}]");
        }
    }

    public override void Flush()
    {
        lock (_lock)
        {
            _originalWriter.Flush();
            _fileWriter.Flush();
        }
    }
}

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