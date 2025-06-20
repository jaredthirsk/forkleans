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

            // Join the game
            var joinResponse = await _httpClient.PostAsJsonAsync(
                $"{_siloUrl}/api/world/join",
                new { playerName = _botName },
                cancellationToken);

            if (!joinResponse.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to join game: {Status}", joinResponse.StatusCode);
                return;
            }

            var joinResult = await joinResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            _playerId = joinResult.GetProperty("playerId").GetString() ?? "";
            _logger.LogInformation("Bot {BotName} joined as player {PlayerId}", _botName, _playerId);

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
            
            // Get current player position
            var stateResponse = await _httpClient.GetAsync(
                $"{_siloUrl}/api/world/player/{_playerId}/state",
                cancellationToken);

            if (stateResponse.IsSuccessStatusCode)
            {
                var playerInfo = await stateResponse.Content.ReadFromJsonAsync<PlayerInfo>(cancellationToken: cancellationToken);
                if (playerInfo != null)
                {
                    var currentPos = playerInfo.Position;
                    var targetPos = targetZone.GetCenter();
                    var distance = Vector2.Distance(currentPos, targetPos);

                    if (distance < 100f) // Close enough to zone center
                    {
                        // Move to next zone
                        _zoneVisitIndex++;
                        _logger.LogInformation("Bot {BotName} reached zone {Zone}, moving to next", _botName, targetZone);
                    }
                    else
                    {
                        // Move towards target zone
                        var direction = (targetPos - currentPos).Normalized();
                        await SendMovement(direction, cancellationToken);
                    }

                    // Shoot occasionally in test mode (once per 2 seconds)
                    if ((DateTime.UtcNow - _lastShootTime).TotalSeconds >= 2.0)
                    {
                        await SendShoot(true, cancellationToken);
                        await Task.Delay(200, cancellationToken);
                        await SendShoot(false, cancellationToken);
                        _lastShootTime = DateTime.UtcNow;
                    }
                }
            }
        }
    }

    private async Task RunNormalMode(CancellationToken cancellationToken)
    {
        // Normal mode: Act like a human player
        var stateResponse = await _httpClient.GetAsync(
            $"{_siloUrl}/api/world/state",
            cancellationToken);

        if (stateResponse.IsSuccessStatusCode)
        {
            _lastWorldState = await stateResponse.Content.ReadFromJsonAsync<WorldState>(cancellationToken: cancellationToken);
            if (_lastWorldState != null)
            {
                var player = _lastWorldState.Entities.FirstOrDefault(e => e.EntityId == _playerId);
                if (player != null)
                {
                    // Find nearest enemy or asteroid
                    var targets = _lastWorldState.Entities
                        .Where(e => (e.Type == EntityType.Enemy || e.Type == EntityType.Asteroid) && e.Health > 0)
                        .OrderBy(e => Vector2.Distance(player.Position, e.Position))
                        .ToList();

                    if (targets.Any())
                    {
                        var target = targets.First();
                        var distance = Vector2.Distance(player.Position, target.Position);
                        var direction = (target.Position - player.Position).Normalized();

                        // Combat behavior
                        if (distance > 200f)
                        {
                            // Move closer
                            await SendMovement(direction, cancellationToken);
                        }
                        else if (distance < 100f)
                        {
                            // Too close, back away
                            await SendMovement(-direction, cancellationToken);
                        }
                        else
                        {
                            // Good distance, strafe
                            var strafeDir = new Vector2(-direction.Y, direction.X);
                            await SendMovement(strafeDir, cancellationToken);
                        }

                        // Shoot at target
                        if (distance < 300f)
                        {
                            await SendShoot(true, cancellationToken);
                            await Task.Delay(100, cancellationToken);
                            await SendShoot(false, cancellationToken);
                        }
                    }
                    else
                    {
                        // No targets, explore
                        var randomAngle = Random.Shared.NextSingle() * MathF.PI * 2;
                        var randomDir = new Vector2(MathF.Cos(randomAngle), MathF.Sin(randomAngle));
                        await SendMovement(randomDir, cancellationToken);
                    }
                }
            }
        }
    }

    private async Task UpdateZoneInfo(CancellationToken cancellationToken)
    {
        try
        {
            var zonesResponse = await _httpClient.GetAsync(
                $"{_siloUrl}/api/world/zones",
                cancellationToken);

            if (zonesResponse.IsSuccessStatusCode)
            {
                _availableZones = await zonesResponse.Content.ReadFromJsonAsync<List<GridSquare>>(cancellationToken: cancellationToken) ?? new();
                _logger.LogDebug("Bot {BotName} found {ZoneCount} available zones", _botName, _availableZones.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update zone info");
        }
    }

    private async Task SendMovement(Vector2 direction, CancellationToken cancellationToken)
    {
        var scaledDirection = direction * 100f; // Scale for movement speed
        await _httpClient.PostAsJsonAsync(
            $"{_siloUrl}/api/world/player/{_playerId}/move",
            new { direction = scaledDirection },
            cancellationToken);
    }

    private async Task SendShoot(bool shooting, CancellationToken cancellationToken)
    {
        await _httpClient.PostAsJsonAsync(
            $"{_siloUrl}/api/world/player/{_playerId}/shoot",
            new { shooting },
            cancellationToken);
    }

    public Task StopAsync()
    {
        _logger.LogInformation("Bot {BotName} stopping...", _botName);
        _httpClient.Dispose();
        return Task.CompletedTask;
    }
}