using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// Add the Orleans silo with Orleans ports exposed
var silo = builder.AddProject<Projects.Shooter_Silo>("shooter-silo")
    .WithEndpoint(30000, 30000, name: "orleans-gateway", scheme: "tcp", isProxied: false) // Orleans gateway port
    .WithEndpoint(11111, 11111, name: "orleans-silo", scheme: "tcp", isProxied: false);   // Orleans silo port

// Add action servers with replicas - they depend on the silo being ready
// Running 9 instances to cover a 3x3 grid of zones
// Create individual instances with specific RPC ports to avoid conflicts
for (int i = 0; i < 9; i++)
{
    var rpcPort = 12000 + i;
    builder.AddProject<Projects.Shooter_ActionServer>($"shooter-actionserver-{i}")
        .WithEnvironment("Orleans__SiloUrl", silo.GetEndpoint("https"))
        .WithEnvironment("Orleans__GatewayEndpoint", silo.GetEndpoint("orleans-gateway"))
        .WithEnvironment("RPC_PORT", rpcPort.ToString())
        .WithEnvironment("ASPIRE_INSTANCE_ID", i.ToString()) // Help identify instances
        .WithReference(silo)
        .WaitFor(silo);
    // Aspire will automatically assign unique HTTP endpoints
}

// Add the Blazor client - it depends on the silo being ready
builder.AddProject<Projects.Shooter_Client>("shooter-client")
    .WithEnvironment("SiloUrl", silo.GetEndpoint("https"))
    .WithReference(silo)
    .WaitFor(silo);

// Add bot instances for testing
for (int i = 0; i < 2; i++)
{
    builder.AddProject<Projects.Shooter_Bot>($"shooter-bot-{i}")
        .WithEnvironment("SiloUrl", silo.GetEndpoint("https"))
        .WithEnvironment("BotCount", "1") // Each instance runs 1 bot
        .WithEnvironment("TestMode", "true")
        .WithReference(silo)
        .WaitFor(silo);
}

builder.Build().Run();
