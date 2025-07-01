using System.Reflection;
using System.Runtime.Loader;
using Orleans;
using Orleans.Hosting;
using UFX.Orleans.SignalRBackplane;
using Microsoft.AspNetCore.SignalR;

// Assembly redirect handler - redirects Microsoft.Orleans.* to Granville.Orleans.*
AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
{
    if (assemblyName.Name?.StartsWith("Microsoft.Orleans") == true)
    {
        var granvilleName = assemblyName.Name.Replace("Microsoft.Orleans", "Granville.Orleans");
        try
        {
            Console.WriteLine($"[Assembly Redirect] {assemblyName.Name} -> {granvilleName}");
            
            // Try to load from the current directory first
            var assemblyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{granvilleName}.dll");
            if (File.Exists(assemblyPath))
            {
                Console.WriteLine($"[Assembly Redirect] Loading from: {assemblyPath}");
                return context.LoadFromAssemblyPath(assemblyPath);
            }
            
            // Fallback to loading by name
            return context.LoadFromAssemblyName(new AssemblyName(granvilleName));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Assembly Redirect] Failed to redirect {assemblyName.Name}: {ex.Message}");
        }
    }
    return null;
};

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddSignalR();

Console.WriteLine("=== Testing UFX SignalR with Granville Orleans ===");
Console.WriteLine("This test demonstrates that UFX.Orleans.SignalRBackplane can work");
Console.WriteLine("with Granville.Orleans.* assemblies via assembly redirection.");
Console.WriteLine();

// Configure Orleans with minimal setup to test UFX integration
builder.Host.UseOrleans(siloBuilder =>
{
    siloBuilder
        .UseLocalhostClustering()
        .Configure<Orleans.Configuration.ClusterOptions>(options =>
        {
            options.ClusterId = "test";
            options.ServiceId = "TestUFXIntegration";
        })
        .AddMemoryGrainStorage(UFX.Orleans.SignalRBackplane.Constants.StorageName)
        .UseInMemoryReminderService()
        .AddSignalRBackplane(); // This is from UFX.Orleans.SignalRBackplane
});

var app = builder.Build();

// Map a simple SignalR hub
app.MapHub<TestHub>("/testhub");

// Add test endpoint
app.MapGet("/", () => "UFX SignalR + Granville Orleans Integration Test Running!");

app.MapGet("/test", (Orleans.IGrainFactory grainFactory) =>
{
    try
    {
        // Try to use Orleans functionality
        var grain = grainFactory.GetGrain<Orleans.IGrainWithIntegerKey>(0);
        return Results.Ok(new 
        { 
            Success = true, 
            Message = "Successfully created grain reference using Granville Orleans assemblies!",
            GrainType = grain.GetType().Name,
            AssemblyName = grain.GetType().Assembly.FullName
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Error: {ex.Message}");
    }
});

Console.WriteLine();
Console.WriteLine("Starting application...");
Console.WriteLine("Visit http://localhost:5212/test to verify the integration");
Console.WriteLine();

app.Run();

public class TestHub : Hub
{
    public async Task SendMessage(string message)
    {
        await Clients.All.SendAsync("ReceiveMessage", message);
    }
}