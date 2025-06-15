using Shooter.Client.Data;
using Shooter.Client.Services;
using Orleans.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSingleton<WeatherForecastService>();

// Add Orleans serializer
builder.Services.AddSerializer();

// Add game services
builder.Services.AddHttpClient<GameClientService>(client =>
{
    var siloUrl = builder.Configuration["SiloUrl"] ?? "https://localhost:61311/";
    if (!siloUrl.EndsWith("/"))
        siloUrl += "/";
    client.BaseAddress = new Uri(siloUrl);
});

// Add UDP-based game client service with HttpClient
builder.Services.AddHttpClient<UdpGameClientService>(client =>
{
    var siloUrl = builder.Configuration["SiloUrl"] ?? "https://localhost:61311/";
    if (!siloUrl.EndsWith("/"))
        siloUrl += "/";
    client.BaseAddress = new Uri(siloUrl);
});

// Configuration for which client to use (HTTP vs UDP)
var useUdp = builder.Configuration.GetValue<bool>("UseUdpClient", true);
builder.Services.AddSingleton<IGameClientProvider>(sp => new GameClientProvider(useUdp));

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