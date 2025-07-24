using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

var configData = new Dictionary<string, string?>
{
    {"Logging:LogLevel:Default", "Information"},
    {"Logging:LogLevel:Granville.Rpc", "Debug"}
};

var config = new ConfigurationBuilder()
    .AddInMemoryCollection(configData)
    .Build();

var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(config);
services.AddLogging(builder =>
{
    builder.AddConfiguration(config.GetSection("Logging"));
    builder.AddConsole();
});

var provider = services.BuildServiceProvider();

// Test different logger names that might be used
var testLoggers = new[]
{
    ("Granville.Rpc", provider.GetService<ILoggerFactory>()!.CreateLogger("Granville.Rpc")),
    ("Granville.Rpc.RpcSerializationSessionFactory", provider.GetService<ILoggerFactory>()!.CreateLogger("Granville.Rpc.RpcSerializationSessionFactory")),
    ("Orleans.Rpc.Client.RpcSerializationSessionFactory", provider.GetService<ILoggerFactory>()!.CreateLogger("Orleans.Rpc.Client.RpcSerializationSessionFactory"))
};

Console.WriteLine("Logger test results:");
foreach (var (name, logger) in testLoggers)
{
    var isEnabled = logger.IsEnabled(LogLevel.Debug);
    Console.WriteLine($"Logger '{name}': Debug enabled = {isEnabled}");
    
    if (isEnabled)
    {
        logger.LogDebug("[TEST] This debug message should appear for {LoggerName}", name);
    }
    else
    {
        Console.WriteLine($"  Debug logging is disabled for {name}");
    }
}