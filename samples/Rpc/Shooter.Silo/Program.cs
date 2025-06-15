using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Shooter.Silo.Controllers;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

// Configure Orleans
builder.Host.UseOrleans(siloBuilder =>
{
    siloBuilder
        .UseLocalhostClustering(
            siloPort: 11111,
            gatewayPort: 30000)
        .Configure<ClusterOptions>(options =>
        {
            options.ClusterId = "dev";
            options.ServiceId = "ShooterDemo";
        })
        .Configure<EndpointOptions>(options =>
        {
            options.AdvertisedIPAddress = IPAddress.Loopback;
        })
        .AddMemoryGrainStorage("worldStore")
        .AddMemoryGrainStorage("playerStore");
});

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// Add a simple endpoint to check if Orleans is ready
app.MapGet("/orleans-ready", (Orleans.IGrainFactory grainFactory) =>
{
    return Results.Ok(new { Ready = true });
});

app.Run();