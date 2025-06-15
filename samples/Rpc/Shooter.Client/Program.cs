using Shooter.Client.Data;
using Shooter.Client.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSingleton<WeatherForecastService>();

// Add Forkleans RPC game client service as singleton
var siloUrl = builder.Configuration["SiloUrl"] ?? "https://localhost:61311/";
if (!siloUrl.EndsWith("/"))
    siloUrl += "/";

// Register HttpClient factory
builder.Services.AddHttpClient();

// Register the RPC game client service
builder.Services.AddSingleton<ForleansRpcGameClientService>(serviceProvider =>
{
    var logger = serviceProvider.GetRequiredService<ILogger<ForleansRpcGameClientService>>();
    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient();
    httpClient.BaseAddress = new Uri(siloUrl);
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    return new ForleansRpcGameClientService(logger, httpClient, configuration);
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