using Shooter.Client;
using Shooter.Client.Data;
using Shooter.Client.Common;

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
builder.Logging.AddProvider(new FileLoggerProvider("../logs/client.log"));

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSingleton<WeatherForecastService>();

// Add Orleans RPC game client service as singleton
var siloUrl = builder.Configuration["SiloUrl"] ?? "https://localhost:61311/";
if (!siloUrl.EndsWith("/"))
    siloUrl += "/";

// Register HttpClient factory
builder.Services.AddHttpClient();

// Register the RPC game client service
builder.Services.AddSingleton<GranvilleRpcGameClientService>(serviceProvider =>
{
    var logger = serviceProvider.GetRequiredService<ILogger<GranvilleRpcGameClientService>>();
    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient();
    httpClient.BaseAddress = new Uri(siloUrl);
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    return new GranvilleRpcGameClientService(logger, httpClient, configuration);
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
