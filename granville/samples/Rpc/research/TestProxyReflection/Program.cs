using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Granville.Rpc;
using Shooter.Shared.RpcInterfaces;

// Simple test to verify the reflection workaround is working

Console.WriteLine("Testing Orleans proxy with RPC - Reflection Workaround");

var builder = Host.CreateApplicationBuilder(args);

// Enable debug logging
builder.Logging.SetMinimumLevel(LogLevel.Debug);
builder.Logging.AddConsole();

// Configure RPC client
builder.Services.UseRpcClient(clientBuilder =>
{
    clientBuilder.UseOrleansClientNetworking("localhost", 11111, 30000);
    clientBuilder.UseLiteNetLibTransport();
});

var host = builder.Build();

try
{
    await host.StartAsync();
    
    var logger = host.Services.GetRequiredService<ILogger<Program>>();
    var rpcClient = host.Services.GetRequiredService<IRpcClient>();
    
    logger.LogInformation("=== Testing Proxy Argument Passing ===");
    
    // Wait for connection
    await Task.Delay(2000);
    
    // Get the game grain
    logger.LogInformation("Getting game grain...");
    var gameGrain = rpcClient.GetGrain<IGameRpcGrain>("game");
    logger.LogInformation("Got grain: {GrainType}", gameGrain.GetType().FullName);
    
    // Test with a unique player ID
    var playerId = $"test-player-{Guid.NewGuid()}";
    logger.LogInformation("Calling ConnectPlayer with playerId: {PlayerId}", playerId);
    
    try
    {
        var result = await gameGrain.ConnectPlayer(playerId);
        
        if (result == "FAILED")
        {
            logger.LogError("❌ ConnectPlayer returned FAILED - server received null argument!");
            logger.LogError("The reflection workaround is NOT working properly.");
        }
        else
        {
            logger.LogInformation("✅ ConnectPlayer returned: {Result}", result);
            logger.LogInformation("The reflection workaround IS working!");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error calling ConnectPlayer");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Host startup failed: {ex}");
}
finally
{
    await host.StopAsync();
}