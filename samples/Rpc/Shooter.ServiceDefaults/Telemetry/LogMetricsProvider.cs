using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Shooter.ServiceDefaults.Telemetry;

/// <summary>
/// Logging provider that captures metrics about log message frequency and levels.
/// </summary>
public class LogMetricsProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, LogMetricsLogger> _loggers = new();
    private static readonly Meter _meter = new("Shooter.Logging", "1.0.0");
    
    // Metrics for log message frequency
    private static readonly Counter<long> _logMessagesTotal = _meter.CreateCounter<long>(
        "log_messages_total",
        description: "Total number of log messages by level and category");
    
    private static readonly Histogram<double> _logMessageRate = _meter.CreateHistogram<double>(
        "log_message_rate_per_minute", 
        unit: "messages/minute",
        description: "Rate of log messages per minute");
    
    // Track log levels
    private static readonly Counter<long> _logMessagesByLevel = _meter.CreateCounter<long>(
        "log_messages_by_level",
        description: "Log messages counted by level (Debug, Info, Warning, Error, Critical)");
    
    // Observable gauge for current rate
    private static readonly ObservableGauge<double> _currentLogRate = _meter.CreateObservableGauge<double>(
        "current_log_rate_per_minute",
        unit: "messages/minute", 
        description: "Current log message rate per minute",
        observeValue: () => CalculateCurrentRate());
    
    private static readonly ConcurrentQueue<DateTime> _recentMessages = new();
    private static readonly object _rateLock = new();
    
    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new LogMetricsLogger(name, RecordLogMessage));
    }

    public void Dispose()
    {
        _loggers.Clear();
        _meter.Dispose();
    }
    
    private static void RecordLogMessage(string categoryName, LogLevel logLevel, EventId eventId)
    {
        var now = DateTime.UtcNow;
        
        // Record total messages
        _logMessagesTotal.Add(1, 
            new KeyValuePair<string, object?>("category", categoryName),
            new KeyValuePair<string, object?>("level", logLevel.ToString()));
        
        // Record by level
        _logMessagesByLevel.Add(1,
            new KeyValuePair<string, object?>("level", logLevel.ToString()));
        
        // Track for rate calculation
        lock (_rateLock)
        {
            _recentMessages.Enqueue(now);
            
            // Clean old messages (older than 1 minute)
            var cutoff = now.AddMinutes(-1);
            while (_recentMessages.TryPeek(out var oldest) && oldest < cutoff)
            {
                _recentMessages.TryDequeue(out _);
            }
        }
    }
    
    private static double CalculateCurrentRate()
    {
        lock (_rateLock)
        {
            var now = DateTime.UtcNow;
            var cutoff = now.AddMinutes(-1);
            
            // Clean old messages
            while (_recentMessages.TryPeek(out var oldest) && oldest < cutoff)
            {
                _recentMessages.TryDequeue(out _);
            }
            
            return _recentMessages.Count; // Messages in the last minute
        }
    }
}

/// <summary>
/// Logger implementation that records metrics for each log message.
/// </summary>
public class LogMetricsLogger : ILogger
{
    private readonly string _categoryName;
    private readonly Action<string, LogLevel, EventId> _recordMetric;

    public LogMetricsLogger(string categoryName, Action<string, LogLevel, EventId> recordMetric)
    {
        _categoryName = categoryName;
        _recordMetric = recordMetric;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        // Record the metric
        _recordMetric(_categoryName, logLevel, eventId);
    }
}

/// <summary>
/// Extension methods for registering log metrics.
/// </summary>
public static class LogMetricsExtensions
{
    /// <summary>
    /// Adds log message metrics to the logging builder.
    /// </summary>
    public static ILoggingBuilder AddLogMetrics(this ILoggingBuilder builder)
    {
        builder.Services.AddSingleton<ILoggerProvider, LogMetricsProvider>();
        return builder;
    }
}