using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

// Configuration
const int InitialActionServerCount = 4;
const int BotCount = 1; // Number of bots to start
const int SiloCount = 1; // Number of silos to start
var transportType = args.FirstOrDefault(arg => arg.StartsWith("--transport="))?.Replace("--transport=", "") ?? "litenetlib";

// Pass environment from parent if available
var aspnetcoreEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

// Add multiple Orleans silos to test SignalR backplane
var silos = new List<IResourceBuilder<ProjectResource>>();
IResourceBuilder<ProjectResource>? primarySilo = null;

// Helper to find next available port block
static int FindAvailablePortBlock(int startBlock, int blockSize = 10)
{
    int currentBlock = startBlock;
    while (currentBlock < 100) // Limit search to prevent infinite loop
    {
        var basePort = 7070 + (currentBlock * blockSize);
        var portsToCheck = new[] { basePort + 1, basePort + 2, basePort + 3 }; // HTTP, HTTPS, Dashboard
        
        bool allAvailable = true;
        foreach (var port in portsToCheck)
        {
            try
            {
                using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
            }
            catch
            {
                allAvailable = false;
                break;
            }
        }
        
        if (allAvailable)
            return currentBlock;
            
        currentBlock++;
    }
    throw new InvalidOperationException($"Could not find available port block starting from block {startBlock}");
}

for (int i = 0; i < SiloCount; i++)
{
    var siloPort = 11111 + i;
    var gatewayPort = 30000 + i;
    
    // Find next available port block
    var blockIndex = FindAvailablePortBlock(i);
    var basePort = 7070 + (blockIndex * 10);
    var httpPort = basePort + 1;        // 7071, 7081, etc.
    var httpsPort = basePort + 2;       // 7072, 7082, etc.
    var dashboardPort = basePort + 3;   // 7073, 7083, etc.
    var siloName = $"shooter-silo-{i}";
    
    var silo = builder.AddProject<Projects.Shooter_Silo>(siloName)
        .WithHttpsEndpoint(httpsPort, httpsPort, isProxied: false)
        .WithHttpEndpoint(httpPort, httpPort, isProxied: false)
        .WithHttpEndpoint(dashboardPort, dashboardPort, name: "orleans-dashboard", isProxied: false)
        .WithEndpoint(gatewayPort, gatewayPort, name: "orleans-gateway", scheme: "tcp", isProxied: false)
        .WithEndpoint(siloPort, siloPort, name: "orleans-silo", scheme: "tcp", isProxied: false)
        .WithEnvironment("Orleans:ClusterId", "shooter-cluster")
        .WithEnvironment("Orleans:ServiceId", "shooter-service")
        .WithEnvironment("Orleans:SiloPort", siloPort.ToString())
        .WithEnvironment("Orleans:GatewayPort", gatewayPort.ToString())
        .WithEnvironment("InitialActionServerCount", InitialActionServerCount.ToString())
        .WithEnvironment("ASPNETCORE_HTTP_PORT", httpPort.ToString())
        .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", builder.Configuration["DOTNET_DASHBOARD_OTLP_ENDPOINT_URL"] ?? "http://localhost:19265");
    
    // Configure clustering - all silos need to know about the primary
    if (i == 0)
    {
        // First silo is the primary
        primarySilo = silo;
        silo.WithEnvironment("Orleans:IsPrimarySilo", "true");
    }
    else
    {
        // Other silos connect to the primary
        silo.WithEnvironment("Orleans:PrimarySiloEndpoint", "localhost:11111");
        if (primarySilo != null)
        {
            silo.WaitFor(primarySilo);
        }
    }
    
    if (!string.IsNullOrEmpty(aspnetcoreEnv))
    {
        silo.WithEnvironment("ASPNETCORE_ENVIRONMENT", aspnetcoreEnv);
    }
    
    silos.Add(silo);
}

// Add action servers with replicas - they depend on the silos being ready
// Running initial instances to cover a grid of zones
// Create individual instances with specific RPC ports to avoid conflicts
var actionServers = new List<IResourceBuilder<ProjectResource>>();
for (int i = 0; i < InitialActionServerCount; i++)
{
    var rpcPort = 12000 + i;
    // Distribute action servers across silos using round-robin
    var targetSilo = silos[i % silos.Count];
    
    var server = builder.AddProject<Projects.Shooter_ActionServer>($"shooter-actionserver-{i}")
        .WithEnvironment("Orleans__SiloUrl", targetSilo.GetEndpoint("https"))
        .WithEnvironment("Orleans__GatewayEndpoint", targetSilo.GetEndpoint("orleans-gateway"))
        .WithEnvironment("Orleans__ClusterId", "shooter-cluster")
        .WithEnvironment("Orleans__ServiceId", "shooter-service")
        .WithEnvironment("RPC_PORT", rpcPort.ToString())
        .WithEnvironment("ASPIRE_INSTANCE_ID", i.ToString()) // Help identify instances
        .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", builder.Configuration["DOTNET_DASHBOARD_OTLP_ENDPOINT_URL"] ?? "http://localhost:19265")
        .WithArgs($"--transport={transportType}")
        .WithReference(targetSilo)
        .WaitFor(targetSilo);
    
    // Pass environment from parent if available
    if (!string.IsNullOrEmpty(aspnetcoreEnv))
    {
        server.WithEnvironment("ASPNETCORE_ENVIRONMENT", aspnetcoreEnv);
    }
    
    actionServers.Add(server);
    // Aspire will automatically assign unique HTTP endpoints
}

// Add the Blazor client - it depends on the primary silo being ready
// Client can connect to any silo through the gateway
builder.AddProject<Projects.Shooter_Client>("shooter-client")
    .WithEnvironment("SiloUrl", primarySilo!.GetEndpoint("https"))
    .WithEnvironment("RpcTransport", transportType)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", builder.Configuration["DOTNET_DASHBOARD_OTLP_ENDPOINT_URL"] ?? "http://localhost:19265")
    .WithReference(primarySilo!)
    .WaitFor(primarySilo!);

// Add bot instances for testing - wait for at least one action server to be ready
for (int i = 0; i < BotCount; i++)
{
    // Distribute bots across silos
    var targetSilo = silos[i % silos.Count];
    
    var bot = builder.AddProject<Projects.Shooter_Bot>($"shooter-bot-{i}")
        .WithEnvironment("SiloUrl", targetSilo.GetEndpoint("https"))
        .WithEnvironment("RpcTransport", transportType)
        .WithEnvironment("TestMode", "true")
        .WithEnvironment("ASPIRE_INSTANCE_ID", i.ToString())
        .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", builder.Configuration["DOTNET_DASHBOARD_OTLP_ENDPOINT_URL"] ?? "http://localhost:19265")
        .WithReference(targetSilo)
        .WaitFor(targetSilo);
    
    // Wait for at least the first action server
    if (actionServers.Count > 0)
    {
        bot.WaitFor(actionServers[0]);
    }
}

var app = builder.Build();

// Improve graceful shutdown handling
Console.CancelKeyPress += (sender, e) =>
{
    Console.WriteLine("\n🔄 Graceful shutdown initiated... Press Ctrl+C again to force quit.");
    e.Cancel = true; // Prevent immediate termination
    
    // Trigger graceful shutdown
    var cts = new CancellationTokenSource();
    cts.CancelAfter(TimeSpan.FromSeconds(30)); // 30 second timeout for graceful shutdown
    
    _ = Task.Run(async () =>
    {
        try
        {
            await app.StopAsync(cts.Token);
            Console.WriteLine("✅ Graceful shutdown completed.");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("⚠️ Graceful shutdown timed out. Forcing exit.");
        }
        Environment.Exit(0);
    });
};

app.Run();
