using Shooter.Client;
using Shooter.Client.Data;
using Shooter.Client.Common;
using Shooter.Client.Common.Services;
using Shooter.Client.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Configure logging
builder.Logging.SetMinimumLevel(LogLevel.Debug);
builder.Logging.AddFilter("Granville.Rpc", LogLevel.Debug);
builder.Logging.AddFilter("Shooter.Client.Common", LogLevel.Debug);
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Routing", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Mvc", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.StaticFiles", LogLevel.Warning);

// Add file logging
var logFileName = "../logs/client.log";
builder.Logging.AddProvider(new FileLoggerProvider(logFileName));

// Add console redirection for stdout and stderr
var consoleLogFileName = logFileName.Replace(".log", "-console.log");
var consoleLogDir = Path.GetDirectoryName(consoleLogFileName);
if (!string.IsNullOrEmpty(consoleLogDir))
{
    Directory.CreateDirectory(consoleLogDir);
}
var consoleWriter = new StreamWriter(consoleLogFileName, append: true) { AutoFlush = true };
var consoleLock = new object();

// Store original console outputs for restoration
var originalOut = Console.Out;
var originalError = Console.Error;

// Redirect console outputs
Console.SetOut(new ConsoleRedirector(originalOut, consoleWriter, "OUT", consoleLock));
Console.SetError(new ConsoleRedirector(originalError, consoleWriter, "ERR", consoleLock));

// Register for cleanup on shutdown
builder.Services.AddSingleton(consoleWriter);
builder.Services.AddHostedService<ConsoleRedirectorCleanupService>();

Console.WriteLine($"Client logging to: {logFileName}");
Console.WriteLine($"Console output logging to: {consoleLogFileName}");

// Set up unhandled exception handlers to ensure they're captured
AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
{
    var ex = args.ExceptionObject as Exception;
    Console.Error.WriteLine($"Unhandled exception in AppDomain: {ex}");
};

TaskScheduler.UnobservedTaskException += (sender, args) =>
{
    Console.Error.WriteLine($"Unobserved task exception: {args.Exception}");
    args.SetObserved(); // Prevent process termination
};

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// Configure SignalR hub options for long-running game sessions
builder.Services.Configure<Microsoft.AspNetCore.SignalR.HubOptions>(options =>
{
    // Increase timeouts for long-running game sessions
    // Default is 30 seconds - too aggressive for gaming
    options.ClientTimeoutInterval = TimeSpan.FromMinutes(10);    // How long server waits for client activity
    options.KeepAliveInterval = TimeSpan.FromSeconds(30);        // How often server pings client
    options.HandshakeTimeout = TimeSpan.FromSeconds(30);         // Handshake timeout
    options.MaximumReceiveMessageSize = 1024 * 1024;             // 1 MB max message size
});

builder.Services.AddSingleton<WeatherForecastService>();

// Add heartbeat monitoring service
builder.Services.AddSingleton<ClientHeartbeatService>();
builder.Services.AddHostedService<ClientHeartbeatService>(provider => provider.GetRequiredService<ClientHeartbeatService>());

// Add Orleans RPC game client service as singleton
var siloUrl = builder.Configuration["SiloUrl"] ?? "https://localhost:61311/";
if (!siloUrl.EndsWith("/"))
    siloUrl += "/";

// Register HttpClient factory
builder.Services.AddHttpClient();

// Register the RPC game client service as SCOPED to prevent shared state between browser instances
// Each SignalR connection (browser tab/window) will get its own instance
builder.Services.AddScoped<GranvilleRpcGameClientService>(serviceProvider =>
{
    var logger = serviceProvider.GetRequiredService<ILogger<GranvilleRpcGameClientService>>();
    var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient();
    httpClient.BaseAddress = new Uri(siloUrl);
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    return new GranvilleRpcGameClientService(logger, httpClient, configuration, loggerFactory);
});

// Register SignalR chat service as SCOPED to match the game client service
builder.Services.AddScoped<SignalRChatService>(serviceProvider =>
{
    var logger = serviceProvider.GetRequiredService<ILogger<SignalRChatService>>();
    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient();
    httpClient.BaseAddress = new Uri(siloUrl);
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    return new SignalRChatService(logger, httpClient, configuration);
});

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();

// Service to cleanup console redirection on shutdown
public class ConsoleRedirectorCleanupService : IHostedService
{
    private readonly StreamWriter _consoleWriter;
    private readonly ILogger<ConsoleRedirectorCleanupService> _logger;

    public ConsoleRedirectorCleanupService(StreamWriter consoleWriter, ILogger<ConsoleRedirectorCleanupService> logger)
    {
        _consoleWriter = consoleWriter;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Cleaning up console redirection...");
        
        try
        {
            // Flush and close the console writer
            _consoleWriter.Flush();
            _consoleWriter.Close();
            _consoleWriter.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during console redirection cleanup");
        }
        
        return Task.CompletedTask;
    }
}
