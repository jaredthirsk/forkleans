using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Granville.Rpc;
using Shooter.Shared.RpcInterfaces;

var builder = Host.CreateApplicationBuilder(args);

// Configure logging to see what's happening
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
    
    logger.LogInformation("Testing proxy argument fix...");
    
    // Wait for connection
    await Task.Delay(2000);
    
    // Test the ConnectPlayer method with a non-null argument
    var playerId = Guid.NewGuid().ToString();
    logger.LogInformation("Calling ConnectPlayer with playerId: {PlayerId}", playerId);
    
    try
    {
        var gameGrain = rpcClient.GetGrain<IGameRpcGrain>("game");
        var result = await gameGrain.ConnectPlayer(playerId);
        
        logger.LogInformation("ConnectPlayer returned: {Result}", result);
        
        if (string.IsNullOrEmpty(result))
        {
            logger.LogError("ConnectPlayer returned null or empty - proxy argument fix may not be working!");
        }
        else
        {
            logger.LogInformation("SUCCESS: ConnectPlayer returned a valid result - proxy argument fix is working!");
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

