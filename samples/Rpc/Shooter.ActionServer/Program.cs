using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Hosting;
using Shooter.ActionServer.Services;
using Shooter.ActionServer.Simulation;
using Shooter.Shared.Models;
using System.Net;
using System.Net.Http.Json;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add a startup delay to allow Orleans silo to fully initialize
builder.Services.AddHostedService<OrleansStartupDelayService>();

// Configure Orleans client
builder.Services.AddOrleansClient((Orleans.Hosting.IClientBuilder clientBuilder) =>
{
    var gatewayEndpoint = builder.Configuration["Orleans:GatewayEndpoint"];
    
    if (!string.IsNullOrEmpty(gatewayEndpoint))
    {
        // Parse the endpoint from Aspire (format: "tcp://host:port" or "host:port")
        var uri = new Uri(gatewayEndpoint);
        var host = uri.Host;
        var port = uri.Port;
        
        clientBuilder
            .UseStaticClustering(new IPEndPoint(IPAddress.Parse(host == "localhost" ? "127.0.0.1" : host), port))
            .Configure<ClusterOptions>(options =>
            {
                options.ClusterId = "dev";
                options.ServiceId = "ShooterDemo";
            });
    }
    else
    {
        // Fallback to localhost clustering for local development without Aspire
        clientBuilder
            .UseLocalhostClustering()
            .Configure<ClusterOptions>(options =>
            {
                options.ClusterId = "dev";
                options.ServiceId = "ShooterDemo";
            });
    }
});

// Add HTTP client for registering with silo
builder.Services.AddHttpClient();

// Add world simulation
builder.Services.AddSingleton<IWorldSimulation, WorldSimulation>();
builder.Services.AddHostedService(provider => (WorldSimulation)provider.GetRequiredService<IWorldSimulation>());

// Add game service
builder.Services.AddSingleton<GameService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<GameService>());

// Add background service for registration
builder.Services.AddHostedService<ActionServerRegistrationService>();

var app = builder.Build();

app.MapDefaultEndpoints();

// Add game endpoints
app.MapGet("/", () => "ActionServer is running");

app.MapGet("/status", (IWorldSimulation simulation) => 
{
    var state = simulation.GetCurrentState();
    return new
    {
        EntityCount = state.Entities.Count,
        Timestamp = state.Timestamp
    };
});

// Game endpoints
app.MapPost("/game/connect/{playerId}", async (string playerId, GameService gameService) =>
{
    var connected = await gameService.ConnectPlayer(playerId);
    return Results.Ok(new { Connected = connected });
});

app.MapDelete("/game/disconnect/{playerId}", async (string playerId, GameService gameService) =>
{
    await gameService.DisconnectPlayer(playerId);
    return Results.Ok();
});

app.MapGet("/game/state", async (GameService gameService) =>
{
    var state = await gameService.GetWorldState();
    return Results.Ok(state);
});

app.MapPost("/game/input/{playerId}", async (string playerId, PlayerInput input, GameService gameService) =>
{
    await gameService.UpdatePlayerInput(playerId, input.MoveDirection, input.IsShooting);
    return Results.Ok();
});

app.MapPost("/game/transfer-entity", async (HttpContext context, IWorldSimulation simulation) =>
{
    var request = await context.Request.ReadFromJsonAsync<EntityTransferRequest>();
    if (request == null)
    {
        return Results.BadRequest("Invalid transfer request");
    }
    
    var success = await simulation.TransferEntityIn(
        request.EntityId,
        request.Type,
        request.SubType,
        request.Position,
        request.Velocity,
        request.Health);
        
    return success ? Results.Ok() : Results.StatusCode(500);
});

app.Run();

// Background service to handle registration with Orleans silo
public class ActionServerRegistrationService : BackgroundService
{
    private readonly ILogger<ActionServerRegistrationService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWorldSimulation _simulation;
    private readonly IConfiguration _configuration;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IServiceProvider _serviceProvider;
    private string? _serverId;
    private string? _siloUrl;

    public ActionServerRegistrationService(
        ILogger<ActionServerRegistrationService> logger,
        IHttpClientFactory httpClientFactory,
        IWorldSimulation simulation,
        IConfiguration configuration,
        IHostApplicationLifetime lifetime,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _simulation = simulation;
        _configuration = configuration;
        _lifetime = lifetime;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for the app to start
        var tcs = new TaskCompletionSource();
        using var registration = _lifetime.ApplicationStarted.Register(() => tcs.SetResult());
        await tcs.Task;
        
        var httpClient = _httpClientFactory.CreateClient();
        
        // Wait a bit for the server to fully start
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        
        // Get the server's actual addresses
        var server = _serviceProvider.GetService<IServer>();
        var addresses = server?.Features.Get<IServerAddressesFeature>()?.Addresses;
        
        string? actualAddress = null;
        int actualPort = 5100;
        
        if (addresses != null && addresses.Any())
        {
            actualAddress = addresses.First();
            _logger.LogInformation("Server listening on: {Addresses}", string.Join(", ", addresses));
            
            var uri = new Uri(actualAddress);
            actualPort = uri.Port;
        }
        else
        {
            // Fallback to environment variable or configuration
            var serverAddresses = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") 
                ?? _configuration["urls"] 
                ?? "http://localhost:5100";
            
            _logger.LogInformation("Using configured addresses: {Addresses}", serverAddresses);
            
            var firstAddress = serverAddresses.Split(';')[0];
            var uri = new Uri(firstAddress);
            actualPort = uri.Port;
        }
        
        _serverId = Guid.NewGuid().ToString();
        _siloUrl = _configuration["Orleans:SiloUrl"] ?? "https://localhost:7071";
        
        _logger.LogInformation("ActionServer starting with ID: {ServerId}, Port: {Port}", _serverId, actualPort);
        
        // Retry registration with exponential backoff
        var retryCount = 0;
        var maxRetries = 10;
        var registered = false;
        
        while (!registered && retryCount < maxRetries && !stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Attempting to register with silo (attempt {Attempt}/{MaxAttempts})", retryCount + 1, maxRetries);
                
                // In Aspire, use the service instance name (e.g., shooter-actionserver-0, shooter-actionserver-1)
                var serviceName = Environment.GetEnvironmentVariable("ASPIRE_SERVICE_INSTANCE_NAME");
                var ipAddress = string.IsNullOrEmpty(serviceName) ? "127.0.0.1" : serviceName;
                
                _logger.LogInformation("Registering with IP/hostname: {IpAddress}", ipAddress);
                
                var response = await httpClient.PostAsJsonAsync(
                    $"{_siloUrl}/api/world/action-servers/register",
                    new { ServerId = _serverId, IpAddress = ipAddress, UdpPort = actualPort },
                    stoppingToken);
                    
                if (response.IsSuccessStatusCode)
                {
                    var serverInfo = await response.Content.ReadFromJsonAsync<ActionServerInfo>(cancellationToken: stoppingToken);
                    if (serverInfo != null)
                    {
                        ((WorldSimulation)_simulation).SetAssignedSquare(serverInfo.AssignedSquare);
                        _logger.LogInformation("Registered with silo, assigned square: ({X}, {Y})", 
                            serverInfo.AssignedSquare.X, serverInfo.AssignedSquare.Y);
                        registered = true;
                    }
                }
                else
                {
                    _logger.LogWarning("Failed to register with silo: {Status}", response.StatusCode);
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Failed to connect to silo, will retry");
            }
            
            if (!registered)
            {
                retryCount++;
                var delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, retryCount), 30)); // Exponential backoff with 30s max
                _logger.LogInformation("Waiting {Delay} before retry", delay);
                await Task.Delay(delay, stoppingToken);
            }
        }
        
        if (!registered)
        {
            _logger.LogError("Failed to register with silo after {MaxRetries} attempts", maxRetries);
            throw new InvalidOperationException("Unable to register with Orleans silo");
        }
        
        try
        {
            // Keep the registration alive
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ActionServer registration failed");
        }
        finally
        {
            // Unregister on shutdown
            if (!string.IsNullOrEmpty(_serverId) && !string.IsNullOrEmpty(_siloUrl))
            {
                try
                {
                    await httpClient.DeleteAsync($"{_siloUrl}/api/world/action-servers/{_serverId}");
                    _logger.LogInformation("Unregistered from silo");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to unregister from silo");
                }
            }
        }
    }
}

public record PlayerInput(Vector2 MoveDirection, bool IsShooting);
public record EntityTransferRequest(string EntityId, EntityType Type, int SubType, Vector2 Position, Vector2 Velocity, float Health);

// Service to delay Orleans client startup to ensure silo is ready
public class OrleansStartupDelayService : IHostedService
{
    private readonly ILogger<OrleansStartupDelayService> _logger;
    private readonly IConfiguration _configuration;

    public OrleansStartupDelayService(ILogger<OrleansStartupDelayService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Only delay if running in Aspire (gateway endpoint is configured)
        if (!string.IsNullOrEmpty(_configuration["Orleans:GatewayEndpoint"]))
        {
            _logger.LogInformation("Delaying Orleans client startup to ensure silo is ready...");
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            _logger.LogInformation("Orleans startup delay complete");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}