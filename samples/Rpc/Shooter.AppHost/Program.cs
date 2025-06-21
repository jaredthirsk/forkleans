using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// Configuration
const int InitialActionServerCount = 9;

// Add the Orleans silo with Orleans ports exposed
var silo = builder.AddProject<Projects.Shooter_Silo>("shooter-silo")
    .WithEndpoint(30000, 30000, name: "orleans-gateway", scheme: "tcp", isProxied: false) // Orleans gateway port
    .WithEndpoint(11111, 11111, name: "orleans-silo", scheme: "tcp", isProxied: false)   // Orleans silo port
    .WithEnvironment("InitialActionServerCount", InitialActionServerCount.ToString());

// Add action servers with replicas - they depend on the silo being ready
// Running initial instances to cover a grid of zones
// Create individual instances with specific RPC ports to avoid conflicts
var actionServers = new List<IResourceBuilder<ProjectResource>>();
for (int i = 0; i < InitialActionServerCount; i++)
{
    var rpcPort = 12000 + i;
    var server = builder.AddProject<Projects.Shooter_ActionServer>($"shooter-actionserver-{i}")
        .WithEnvironment("Orleans__SiloUrl", silo.GetEndpoint("https"))
        .WithEnvironment("Orleans__GatewayEndpoint", silo.GetEndpoint("orleans-gateway"))
        .WithEnvironment("RPC_PORT", rpcPort.ToString())
        .WithEnvironment("ASPIRE_INSTANCE_ID", i.ToString()) // Help identify instances
        .WithReference(silo)
        .WaitFor(silo);
    actionServers.Add(server);
    // Aspire will automatically assign unique HTTP endpoints
}

// Add the Blazor client - it depends on the silo being ready
builder.AddProject<Projects.Shooter_Client>("shooter-client")
    .WithEnvironment("SiloUrl", silo.GetEndpoint("https"))
    .WithReference(silo)
    .WaitFor(silo);

// Add bot instances for testing - wait for at least one action server to be ready
for (int i = 0; i < 2; i++)
{
    var bot = builder.AddProject<Projects.Shooter_Bot>($"shooter-bot-{i}")
        .WithEnvironment("SiloUrl", silo.GetEndpoint("https"))
        .WithEnvironment("BotName", $"TestBot_{i.ToString()}")
        .WithEnvironment("TestMode", "true")
        .WithReference(silo)
        .WaitFor(silo);
    
    // Wait for at least the first action server
    if (actionServers.Count > 0)
    {
        bot.WaitFor(actionServers[0]);
    }
}

builder.Build().Run();
