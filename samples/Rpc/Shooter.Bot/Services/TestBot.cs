using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Shooter.Shared.Models;

namespace Shooter.Bot.Services;

public class TestBot
{
    private readonly ILogger _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _botName;
    private readonly string _siloUrl;
    private readonly bool _testMode;
    private readonly HttpClient _httpClient;
    private string _playerId = "";
    private GridSquare _currentZone = new(0, 0);
    private List<GridSquare> _availableZones = new();
    private int _zoneVisitIndex = 0;
    private DateTime _lastShootTime = DateTime.UtcNow;
    private WorldState? _lastWorldState;

    public TestBot(
        ILogger logger,
        IHttpClientFactory httpClientFactory,
        string botName,
        string siloUrl,
        bool testMode)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _botName = botName;
        _siloUrl = siloUrl;
        _testMode = testMode;
        _httpClient = httpClientFactory.CreateClient();
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Bot {BotName} starting...", _botName);

            // Join the game with retries
            bool joined = false;
            int retryCount = 0;
            const int maxRetries = 5;
            
            while (!joined && retryCount < maxRetries && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var joinResponse = await _httpClient.PostAsJsonAsync(
                        $"{_siloUrl}/api/world/players/register",
                        new { PlayerId = Guid.NewGuid().ToString(), Name = _botName },
                        cancellationToken);

                    if (joinResponse.IsSuccessStatusCode)
                    {
                        var joinResult = await joinResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
                        var playerInfo = joinResult.GetProperty("playerInfo");
                        _playerId = playerInfo.GetProperty("playerId").GetString() ?? "";
                        _logger.LogInformation("Bot {BotName} joined as player {PlayerId}", _botName, _playerId);
                        joined = true;
                    }
                    else
                    {
                        _logger.LogWarning("Failed to join game: {Status}. Retrying {Retry}/{MaxRetries}...", 
                            joinResponse.StatusCode, retryCount + 1, maxRetries);
                        retryCount++;
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount)), cancellationToken); // Exponential backoff
                    }
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(ex, "HTTP error joining game. Retrying {Retry}/{MaxRetries}...", 
                        retryCount + 1, maxRetries);
                    retryCount++;
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount)), cancellationToken);
                }
            }
            
            if (!joined)
            {
                _logger.LogError("Failed to join game after {MaxRetries} retries", maxRetries);
                return;
            }

            // Get initial zone assignment
            await UpdateZoneInfo(cancellationToken);

            // Main bot loop
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (_testMode)
                    {
                        await RunTestMode(cancellationToken);
                    }
                    else
                    {
                        await RunNormalMode(cancellationToken);
                    }

                    await Task.Delay(100, cancellationToken); // Update rate
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in bot loop");
                    await Task.Delay(1000, cancellationToken); // Back off on error
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Bot {BotName} cancelled", _botName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bot {BotName} crashed", _botName);
        }
    }

    private async Task RunTestMode(CancellationToken cancellationToken)
    {
        // Test mode: Methodically visit every zone
        await UpdateZoneInfo(cancellationToken);

        if (_availableZones.Any())
        {
            // Move through zones in order
            var targetZone = _availableZones[_zoneVisitIndex % _availableZones.Count];
            
            // TODO: Implement proper RPC connection to ActionServer
            // For now, just cycle through zones
            _zoneVisitIndex++;
            _logger.LogInformation("Bot {BotName} moving to zone index {Index}", _botName, _zoneVisitIndex);
            await Task.Delay(5000, cancellationToken); // Wait 5 seconds before next zone
        }
    }

    private async Task RunNormalMode(CancellationToken cancellationToken)
    {
        // TODO: Implement proper RPC connection to ActionServer
        // For now, just log status
        _logger.LogDebug("Bot {BotName} in normal mode", _botName);
        await Task.Delay(1000, cancellationToken);
    }

    private async Task UpdateZoneInfo(CancellationToken cancellationToken)
    {
        try
        {
            // TODO: Get zones from RPC connection
            // For now, hardcode a 3x3 grid
            _availableZones.Clear();
            for (int x = 0; x < 3; x++)
            {
                for (int y = 0; y < 3; y++)
                {
                    _availableZones.Add(new GridSquare(x, y));
                }
            }
            _logger.LogDebug("Bot {BotName} using hardcoded {ZoneCount} zones", _botName, _availableZones.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update zone info");
        }
    }

    private Task SendMovement(Vector2 direction, CancellationToken cancellationToken)
    {
        // TODO: Send via RPC connection
        _logger.LogDebug("Bot {BotName} would move in direction {Direction}", _botName, direction);
        return Task.CompletedTask;
    }

    private Task SendShoot(bool shooting, CancellationToken cancellationToken)
    {
        // TODO: Send via RPC connection
        _logger.LogDebug("Bot {BotName} would shoot: {Shooting}", _botName, shooting);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _logger.LogInformation("Bot {BotName} stopping...", _botName);
        _httpClient.Dispose();
        return Task.CompletedTask;
    }
}