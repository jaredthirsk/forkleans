using Shooter.Client.Data;
using Shooter.Client.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSingleton<WeatherForecastService>();

// Add game services
builder.Services.AddHttpClient<GameClientService>(client =>
{
    var siloUrl = builder.Configuration["SiloUrl"] ?? "https://localhost:61311/";
    if (!siloUrl.EndsWith("/"))
        siloUrl += "/";
    client.BaseAddress = new Uri(siloUrl);
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