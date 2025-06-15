var builder = DistributedApplication.CreateBuilder(args);

// Add the Orleans silo with Orleans ports exposed
var silo = builder.AddProject<Projects.Shooter_Silo>("shooter-silo")
    .WithEndpoint(30000, 30000, name: "orleans-gateway", scheme: "tcp", isProxied: false) // Orleans gateway port
    .WithEndpoint(11111, 11111, name: "orleans-silo", scheme: "tcp", isProxied: false);   // Orleans silo port

// Add action servers with replicas - they depend on the silo being ready
var actionServer = builder.AddProject<Projects.Shooter_ActionServer>("shooter-actionserver")
    .WithEnvironment("Orleans__SiloUrl", silo.GetEndpoint("https"))
    .WithEnvironment("Orleans__GatewayEndpoint", silo.GetEndpoint("orleans-gateway"))
    .WithReference(silo)
    .WaitFor(silo)
    .WithReplicas(2); // Run 2 instances

// Add the Blazor client - it depends on the silo being ready
builder.AddProject<Projects.Shooter_Client>("shooter-client")
    .WithEnvironment("SiloUrl", silo.GetEndpoint("https"))
    .WithReference(silo)
    .WaitFor(silo);

builder.Build().Run();
