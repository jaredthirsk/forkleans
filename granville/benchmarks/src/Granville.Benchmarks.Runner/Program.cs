using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Granville.Benchmarks.Runner.Services;
using Granville.Benchmarks.Core.Workloads;
using Granville.Benchmarks.Core.Transport;
using Granville.Benchmarks.EndToEnd.Workloads;

namespace Granville.Benchmarks.Runner
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((context, config) =>
                {
                    config.AddJsonFile("appsettings.json", optional: false);
                    config.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true);
                    
                    // Support custom configuration files passed as command line arguments
                    if (args.Length > 0 && args[0].EndsWith(".json"))
                    {
                        config.AddJsonFile(args[0], optional: false);
                    }
                    
                    config.AddCommandLine(args);
                })
                .ConfigureServices((context, services) =>
                {
                    // Register benchmark services
                    services.AddSingleton<BenchmarkOrchestrator>();
                    services.AddSingleton<ResultsExporter>();
                    services.AddSingleton<NetworkEmulator>();
                    
                    // Register workloads
                    services.AddTransient<IWorkload, FpsGameWorkload>();
                    services.AddTransient<IWorkload, MobaGameWorkload>();
                    services.AddTransient<IWorkload, MmoGameWorkload>();
                    services.AddTransient<IWorkload, StressTestWorkload>();
                    
                    // Configure options
                    services.Configure<BenchmarkOptions>(context.Configuration.GetSection("BenchmarkOptions"));
                })
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.AddFile("logs/benchmark-{Date}.log");
                })
                .Build();
            
            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Granville RPC Benchmark Runner starting...");
            
            try
            {
                var orchestrator = host.Services.GetRequiredService<BenchmarkOrchestrator>();
                await orchestrator.RunBenchmarksAsync(CancellationToken.None);
                
                logger.LogInformation("All benchmarks completed successfully");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Benchmark runner failed");
                Environment.Exit(1);
            }
        }
    }
    
    public static class LoggingBuilderExtensions
    {
        public static ILoggingBuilder AddFile(this ILoggingBuilder builder, string pathFormat)
        {
            // Simple file logging extension
            builder.AddProvider(new FileLoggerProvider(pathFormat));
            return builder;
        }
    }
    
    public class FileLoggerProvider : ILoggerProvider
    {
        private readonly string _pathFormat;
        private readonly Dictionary<string, FileLogger> _loggers = new();
        
        public FileLoggerProvider(string pathFormat)
        {
            _pathFormat = pathFormat;
        }
        
        public ILogger CreateLogger(string categoryName)
        {
            if (!_loggers.TryGetValue(categoryName, out var logger))
            {
                logger = new FileLogger(categoryName, _pathFormat);
                _loggers[categoryName] = logger;
            }
            return logger;
        }
        
        public void Dispose()
        {
            foreach (var logger in _loggers.Values)
            {
                logger.Dispose();
            }
            _loggers.Clear();
        }
    }
    
    public class FileLogger : ILogger, IDisposable
    {
        private readonly string _categoryName;
        private readonly string _filePath;
        private readonly object _lock = new();
        private StreamWriter? _writer;
        
        public FileLogger(string categoryName, string pathFormat)
        {
            _categoryName = categoryName;
            _filePath = pathFormat.Replace("{Date}", DateTime.Now.ToString("yyyyMMdd"));
            
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
        
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
        
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;
                
            lock (_lock)
            {
                _writer ??= new StreamWriter(_filePath, append: true) { AutoFlush = true };
                
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var message = formatter(state, exception);
                _writer.WriteLine($"[{timestamp}] [{logLevel}] [{_categoryName}] {message}");
                
                if (exception != null)
                {
                    _writer.WriteLine($"Exception: {exception}");
                }
            }
        }
        
        public void Dispose()
        {
            lock (_lock)
            {
                _writer?.Dispose();
                _writer = null;
            }
        }
    }
}