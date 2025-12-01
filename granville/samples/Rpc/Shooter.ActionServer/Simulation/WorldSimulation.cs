using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shooter.Shared.GrainInterfaces;
using Shooter.Shared.Models;
using Shooter.Shared.RpcInterfaces;
using System.Linq;
using Shooter.ActionServer.Services;

namespace Shooter.ActionServer.Simulation;

public class WorldSimulation : BackgroundService, IWorldSimulation
{
    private readonly ILogger<WorldSimulation> _logger;
    private readonly Orleans.IClusterClient _orleansClient;
    private readonly CrossZoneRpcService _crossZoneRpc;
    private readonly ConcurrentDictionary<string, SimulatedEntity> _entities = new();
    private readonly ConcurrentDictionary<string, PlayerInput> _playerInputs = new();
    private readonly Random _random = new();
    private GridSquare _assignedSquare = new GridSquare(0, 0);
    private DateTime _lastUpdate = DateTime.UtcNow;
    private int _nextEntityId = 1;
    private HashSet<GridSquare> _availableZones = new();
    private DateTime _lastZoneCheck = DateTime.MinValue;
    private long _sequenceNumber = 0;
    private TaskCompletionSource<bool> _zoneAssignedTcs = new();
    private readonly DateTime _startTime = DateTime.UtcNow;
    private GamePhase _currentPhase = GamePhase.Playing;
    private DateTime _gameOverTime = DateTime.MinValue;
    private DateTime _lastEnemyDeathTime = DateTime.MinValue;
    private DateTime _victoryPauseTime = DateTime.MinValue;
    private bool _allEnemiesDefeated = false;
    private readonly ConcurrentDictionary<string, int> _playerRespawnCounts = new();
    private readonly ConcurrentDictionary<string, PendingBulletInfo> _pendingBullets = new();
    // Track bullets we've handed off to other zones - prevents re-accepting bullets that exited our zone.
    // This eliminates continuous oscillation where both zones simulate the same bullet.
    // Note: One-time position jumps during handoff are acceptable (see docs/historical/ZONE_TRANSITION_FIXES_SUMMARY.md)
    private readonly ConcurrentDictionary<string, DateTime> _handedOffBullets = new();
    private readonly List<DamageEvent> _damageEvents = new();
    private readonly object _damageEventsLock = new();
    private readonly GameEventBroker _gameEventBroker;
    private Action<string>? _onPlayerTimeoutRemoved;
    
    // FPS tracking
    private readonly Queue<DateTime> _frameTimestamps = new();
    private readonly object _fpsLock = new();
    private double _currentFps = 0;

    public WorldSimulation(ILogger<WorldSimulation> logger, Orleans.IClusterClient orleansClient, CrossZoneRpcService crossZoneRpc, GameEventBroker gameEventBroker)
    {
        _logger = logger;
        _orleansClient = orleansClient;
        _crossZoneRpc = crossZoneRpc;
        _gameEventBroker = gameEventBroker;
    }
    
    public void SetPlayerTimeoutCallback(Action<string> callback)
    {
        _onPlayerTimeoutRemoved = callback;
    }

    public void SetAssignedSquare(GridSquare square)
    {
        _assignedSquare = square;
        _logger.LogInformation("Assigned to grid square: {X}, {Y}", square.X, square.Y);
        _zoneAssignedTcs.TrySetResult(true);
    }
    
    public GridSquare GetAssignedSquare()
    {
        return _assignedSquare;
    }

    public async Task<bool> AddPlayer(string playerId)
    {
        try
        {
            // Check if player already exists
            if (_entities.ContainsKey(playerId))
            {
                var existingEntity = _entities[playerId];
                _logger.LogWarning("[DUPLICATE_PLAYER] Player {PlayerId} ({PlayerName}) already exists in simulation at position {Position} with state {State}, health {Health}, velocity {Velocity}. Request to add rejected.", 
                    playerId, existingEntity.PlayerName, existingEntity.Position, existingEntity.State, existingEntity.Health, existingEntity.Velocity);
                
                // Check if existing player should be removed to allow reconnection
                bool shouldRemoveExisting = false;
                string removalReason = "";
                
                // Remove if dead or has no health
                if (existingEntity.State == EntityStateType.Dead || existingEntity.Health <= 0)
                {
                    shouldRemoveExisting = true;
                    removalReason = "dead/no health";
                }
                // Remove if player hasn't sent input recently (likely a stale connection)
                else if (_playerInputs.ContainsKey(playerId))
                {
                    var input = _playerInputs[playerId];
                    var timeSinceInput = DateTime.UtcNow - input.LastUpdated;
                    if (timeSinceInput.TotalSeconds > 10) // More aggressive than the 30s timeout
                    {
                        shouldRemoveExisting = true;
                        removalReason = $"stale connection ({timeSinceInput.TotalSeconds:F1}s since input)";
                    }
                }
                
                if (shouldRemoveExisting)
                {
                    _logger.LogInformation("[DUPLICATE_PLAYER] Removing existing player {PlayerId} ({Reason}) to allow reconnection", 
                        playerId, removalReason);
                    _entities.TryRemove(playerId, out _);
                    _playerInputs.TryRemove(playerId, out _);
                    // Continue with adding the new player
                }
                else
                {
                    _logger.LogWarning("[DUPLICATE_PLAYER] Existing player {PlayerId} is still active, rejecting duplicate connection", playerId);
                    return false; // Return false to indicate the player was not added
                }
            }
            
            _logger.LogInformation("AddPlayer called for {PlayerId}", playerId);
            var playerGrain = _orleansClient.GetGrain<IPlayerGrain>(playerId);
            _logger.LogInformation("Got player grain reference for {PlayerId}", playerId);
            
            
            // Retry up to 3 times with delays to ensure position is updated
            PlayerInfo? playerInfo = null;
            for (int i = 0; i < 3; i++)
            {
                playerInfo = await playerGrain.GetInfo();
                _logger.LogInformation("Got player info on attempt {Attempt}: {PlayerId} - Name: {Name}, Position: {Position}, Health: {Health}", 
                    i + 1, playerId, playerInfo?.Name ?? "null", playerInfo?.Position ?? Vector2.Zero, playerInfo?.Health ?? 0);
                
                // Check if the position looks valid (not at 0,0 unless that's actually in the zone)
                var playerZone = playerInfo != null ? GridSquare.FromPosition(playerInfo.Position) : null;
                if (playerZone == _assignedSquare || i == 2) // Accept on last try regardless
                {
                    break;
                }
                
                _logger.LogWarning("Player {PlayerId} position {Position} is in zone {PlayerZone} but we are zone {AssignedZone}, retrying...", 
                    playerId, playerInfo?.Position ?? Vector2.Zero, playerZone, _assignedSquare);
                    
                await Task.Delay(100); // Wait 100ms before retry
            }
            
            if (playerInfo == null)
            {
                _logger.LogError("Failed to get player info for {PlayerId}", playerId);
                return false;
            }
            
            if (string.IsNullOrEmpty(playerInfo.Name))
            {
                _logger.LogWarning("Player {PlayerId} has no name, may not be initialized", playerId);
            }
            
            _logger.LogInformation("Retrieved player info: {PlayerId} at position {Position} with health {Health}", 
                playerId, playerInfo.Position, playerInfo.Health);
            
            // Determine if this is a bot based on the name pattern
            bool isBot = System.Text.RegularExpressions.Regex.IsMatch(
                playerInfo.Name, 
                @"^(LiteNetLib|Ruffles)(Test)?\d+$", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            var entity = new SimulatedEntity
            {
                EntityId = playerId,
                Type = EntityType.Player,
                SubType = isBot ? 1 : 0, // 1 for bots, 0 for human players
                Position = playerInfo.Position,
                Velocity = playerInfo.Velocity,
                Health = playerInfo.Health,
                Rotation = 0,
                State = EntityStateType.Active,
                StateTimer = 0f,
                PlayerName = playerInfo.Name, // Store the player name
                Team = playerInfo.Team // Set the player's team
            };
            
            _entities[playerId] = entity;
            _playerInputs[playerId] = new PlayerInput { LastUpdated = DateTime.UtcNow };
            
            _logger.LogInformation("Player {PlayerId} ({PlayerName}) added to simulation at position {Position} in zone {Zone}", 
                playerId, playerInfo.Name, entity.Position, _assignedSquare);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add player {PlayerId}", playerId);
            return false;
        }
    }

    public void RemovePlayer(string playerId)
    {
        if (_entities.TryRemove(playerId, out var entity))
        {
            _logger.LogInformation("[REMOVE_PLAYER] Player {PlayerId} removed from zone {Zone} at position {Position}", 
                playerId, _assignedSquare, entity.Position);
        }
        else
        {
            _logger.LogWarning("[REMOVE_PLAYER] Attempted to remove non-existent player {PlayerId} from zone {Zone}", 
                playerId, _assignedSquare);
        }
        
        if (_playerInputs.TryRemove(playerId, out _))
        {
            _logger.LogDebug("[REMOVE_PLAYER] Player {PlayerId} input tracking removed", playerId);
        }
    }

    public void UpdatePlayerInput(string playerId, Vector2 moveDirection, bool isShooting)
    {
        // First check if player exists in our zone
        if (!_entities.ContainsKey(playerId))
        {
            // Player not in this zone - ignore input
            _logger.LogDebug("[INPUT_REJECTED] Player {PlayerId} not in zone {Zone}, ignoring input", playerId, _assignedSquare);
            return;
        }
        
        if (_playerInputs.TryGetValue(playerId, out var input))
        {
            input.MoveDirection = moveDirection;
            input.IsShooting = isShooting;
            input.LastUpdated = DateTime.UtcNow;
            
            if (moveDirection.Length() > 0)
            {
                _logger.LogDebug("Player {PlayerId} input: Move={Move}, Shoot={Shoot}", 
                    playerId, moveDirection, isShooting);
            }
        }
        else
        {
            // Player exists in entities but not in inputs (race condition during initialization)
            _logger.LogDebug("Adding input tracking for player {PlayerId}", playerId);
            _playerInputs[playerId] = new PlayerInput 
            { 
                MoveDirection = moveDirection, 
                IsShooting = isShooting,
                LastUpdated = DateTime.UtcNow
            };
        }
    }
    
    public void UpdatePlayerInputEx(string playerId, Vector2? moveDirection, Vector2? shootDirection)
    {
        // First check if player exists in our zone
        if (!_entities.ContainsKey(playerId))
        {
            // Player not in this zone - ignore input
            _logger.LogDebug("[INPUT_REJECTED] Player {PlayerId} not in zone {Zone}, ignoring input", playerId, _assignedSquare);
            return;
        }
        
        if (_playerInputs.TryGetValue(playerId, out var input))
        {
            if (moveDirection.HasValue)
            {
                input.MoveDirection = moveDirection.Value;
            }
            
            input.ShootDirection = shootDirection;
            input.IsShooting = shootDirection.HasValue;
            input.LastUpdated = DateTime.UtcNow;
            
            if (moveDirection.HasValue && moveDirection.Value.Length() > 0)
            {
                _logger.LogDebug("Player {PlayerId} input: Move={Move}, ShootDir={ShootDir}", 
                    playerId, moveDirection, shootDirection);
            }
        }
        else
        {
            // Player exists in entities but not in inputs (race condition during initialization)
            _logger.LogDebug("Adding input tracking for player {PlayerId}", playerId);
            _playerInputs[playerId] = new PlayerInput 
            { 
                MoveDirection = moveDirection ?? Vector2.Zero,
                ShootDirection = shootDirection,
                IsShooting = shootDirection.HasValue,
                LastUpdated = DateTime.UtcNow
            };
        }
    }

    public WorldState GetCurrentState()
    {
        var entities = _entities.Values
            .Select(e => new EntityState(
                e.EntityId,
                e.Type,
                e.Position,
                e.Velocity,
                e.Health,
                e.Rotation,
                e.SubType,
                e.State,
                e.StateTimer,
                e.PlayerName,
                e.Team))
            .ToList();
        
        // Log entity counts by type for debugging
        var entityCounts = entities.GroupBy(e => e.Type)
            .ToDictionary(g => g.Key, g => g.Count());
        
        // Check for entities outside our zone
        var outsideEntities = entities.Where(e => GridSquare.FromPosition(e.Position) != _assignedSquare).ToList();
        if (outsideEntities.Any())
        {
            _logger.LogDebug("GetCurrentState: Found {Count} entities outside assigned zone {Zone}: {Entities}", 
                outsideEntities.Count, _assignedSquare, 
                string.Join(", ", outsideEntities.Select(e => $"{e.Type} {e.EntityId} at {e.Position} (zone {GridSquare.FromPosition(e.Position)})")));
        }
        
        if (entities.Count() > 0)
        {
            _logger.LogDebug("GetCurrentState returning {Total} entities: Players={Players}, Enemies={Enemies}, Bullets={Bullets}, Explosions={Explosions}, Factories={Factories}, Asteroids={Asteroids}",
                entities.Count(),
                entityCounts.GetValueOrDefault(EntityType.Player, 0),
                entityCounts.GetValueOrDefault(EntityType.Enemy, 0),
                entityCounts.GetValueOrDefault(EntityType.Bullet, 0),
                entityCounts.GetValueOrDefault(EntityType.Explosion, 0),
                entityCounts.GetValueOrDefault(EntityType.Factory, 0),
                entityCounts.GetValueOrDefault(EntityType.Asteroid, 0));
        }
            
        // Increment sequence number for each state update
        var sequenceNumber = Interlocked.Increment(ref _sequenceNumber);
        return new WorldState(entities, DateTime.UtcNow, sequenceNumber);
    }
    
    public GamePhase GetCurrentPhase()
    {
        return _currentPhase;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for zone assignment before spawning entities
        _logger.LogInformation("Waiting for zone assignment before spawning entities...");
        await _zoneAssignedTcs.Task;
        _logger.LogInformation("Zone assigned: {Zone}. Spawning initial entities.", _assignedSquare);
        
        // Spawn factories first
        var factoryCount = _random.Next(1, 3); // 1-2 factories
        _logger.LogInformation("Spawning {Count} factories in zone {Zone}", factoryCount, _assignedSquare);
        SpawnFactories(factoryCount);
        
        // Spawn asteroids near borders
        SpawnAsteroids();
        
        // Spawn some initial enemies of different types
        _logger.LogInformation("Spawning initial enemies in zone {Zone}", _assignedSquare);
        SpawnEnemies(2, EnemySubType.Kamikaze);
        SpawnEnemies(2, EnemySubType.Sniper);
        SpawnEnemies(1, EnemySubType.Strafing);
        SpawnEnemies(1, EnemySubType.Scout);
        _logger.LogInformation("Initial enemy spawn complete. Total entities: {Count}", _entities.Count);
        
        
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var deltaTime = (float)(now - _lastUpdate).TotalSeconds;
            _lastUpdate = now;
            
            // Track FPS
            UpdateFps(now);
            
            // Update simulation based on current phase
            if (_currentPhase == GamePhase.Playing)
            {
                await UpdatePhysicsAsync(deltaTime);
                ActivatePendingBullets(); // Activate any pending bullets that entered our zone
                UpdateAI(deltaTime);
                CheckCollisions();
                UpdateEntityStates(deltaTime);
                CleanupDeadEntities();
                
                // Check for game over condition
                await CheckGameOverCondition();
                
                // Spawn new enemies occasionally
                if (_random.NextDouble() < 0.0005) // 0.05% chance per frame (50% reduction)
                {
                    // Weighted enemy type selection - favor scouts slightly
                    var roll = _random.NextDouble();
                    EnemySubType enemyType;
                    if (roll < 0.35) // 35% scouts (increased from 25%)
                        enemyType = EnemySubType.Scout;
                    else if (roll < 0.60) // 25% kamikaze
                        enemyType = EnemySubType.Kamikaze;
                    else if (roll < 0.80) // 20% sniper
                        enemyType = EnemySubType.Sniper;
                    else // 20% strafing
                        enemyType = EnemySubType.Strafing;
                        
                    SpawnEnemies(1, enemyType);
                }
            }
            else if (_currentPhase == GamePhase.VictoryPause)
            {
                // During victory pause, just update physics but no player input processing
                await UpdatePhysicsAsync(deltaTime);
                UpdateEntityStates(deltaTime);
                CleanupDeadEntities();
                
                // Send countdown messages and check if pause is over
                await HandleVictoryPause();
            }
            else if (_currentPhase == GamePhase.GameOver)
            {
                // During game over, just update physics to let things settle
                await UpdatePhysicsAsync(deltaTime);
                UpdateEntityStates(deltaTime);
                
                // Check if it's time to restart
                if ((now - _gameOverTime).TotalSeconds >= 15)
                {
                    await RestartGame();
                }
            }
            else if (_currentPhase == GamePhase.Restarting)
            {
                // Do nothing during restart transition
            }
            
            await Task.Delay(16, stoppingToken); // ~60 FPS
        }
    }

    private async Task UpdatePhysicsAsync(float deltaTime)
    {
        // Log current state for debugging
        var playerCount = _entities.Count(e => e.Value.Type == EntityType.Player);
        var inputCount = _playerInputs.Count;
        if (playerCount > 0 || inputCount > 0)
        {
            _logger.LogDebug("UpdatePhysicsAsync: {PlayerCount} players, {InputCount} inputs tracked", playerCount, inputCount);
        }
        
        // Update player positions based on input
        foreach (var (playerId, input) in _playerInputs)
        {
            if (_entities.TryGetValue(playerId, out var player))
            {
                // Skip dead/respawning players
                if (player.State == EntityStateType.Dead || player.State == EntityStateType.Respawning)
                    continue;
                
                var speed = 80f; // units per second (reduced by 20% from original 100)
                var oldPos = player.Position;
                player.Velocity = input.MoveDirection.Normalized() * speed;
                var newPos = player.Position + player.Velocity * deltaTime;
                
                // Check if the new position would be in our assigned zone
                var newZone = GridSquare.FromPosition(newPos);
                
                if (newZone == _assignedSquare)
                {
                    // Still in our zone, allow movement
                    player.Position = newPos;
                }
                else
                {
                    // Player is trying to leave our zone
                    _logger.LogInformation("[ZONE_BOUNDARY] Player {PlayerId} attempting to move from zone ({CurrentX},{CurrentY}) to zone ({NewX},{NewY}) at position {Position}", 
                        playerId, _assignedSquare.X, _assignedSquare.Y, newZone.X, newZone.Y, newPos);
                    
                    var isNewZoneAvailable = await IsZoneAvailable(newZone);
                    
                    if (isNewZoneAvailable)
                    {
                        // Another server handles that zone - allow the movement
                        player.Position = newPos;
                        _logger.LogInformation("Player {PlayerId} moving from zone {OldZone} to {NewZone} - position updated to {Position}", 
                            playerId, _assignedSquare, newZone, newPos);
                        
                        // Immediately initiate transfer when crossing zone boundaries
                        _ = Task.Run(async () => {
                            try
                            {
                                var worldManager = _orleansClient.GetGrain<IWorldManagerGrain>(0);
                                
                                // First update the position and velocity
                                await worldManager.UpdatePlayerPositionAndVelocity(playerId, newPos, player.Velocity);
                                _logger.LogInformation("Updated WorldManager position for player {PlayerId} to {Position} with velocity {Velocity} in zone {Zone}", 
                                    playerId, newPos, player.Velocity, newZone);
                                
                                // Then immediately initiate the transfer
                                var transferInfo = await worldManager.InitiatePlayerTransfer(playerId, newPos);
                                if (transferInfo?.NewServer != null)
                                {
                                    _logger.LogInformation("[ZONE_TRANSITION_IMMEDIATE] Player {PlayerId} needs immediate transfer to server {ServerId} for zone {Zone}", 
                                        playerId, transferInfo.NewServer.ServerId, transferInfo.NewServer.AssignedSquare);
                                    
                                    // The zone monitor will pick this up and complete the transfer
                                }
                                
                                // Also update the player grain position
                                var playerGrain = _orleansClient.GetGrain<IPlayerGrain>(playerId);
                                await playerGrain.UpdatePosition(newPos, player.Velocity);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to initiate player transfer in WorldManager");
                            }
                        });
                    }
                    else
                    {
                        // No server for that zone - block movement
                        _logger.LogDebug("[ZONE_BLOCKED] Player {PlayerId} blocked from entering unavailable zone ({ZoneX},{ZoneY}) at position {Position}", 
                            playerId, newZone.X, newZone.Y, newPos);
                        player.Velocity = Vector2.Zero;
                        
                        // Keep player at zone boundary
                        var (zoneMin, zoneMax) = _assignedSquare.GetBounds();
                        player.Position = new Vector2(
                            Math.Clamp(player.Position.X, zoneMin.X, zoneMax.X - 0.1f),
                            Math.Clamp(player.Position.Y, zoneMin.Y, zoneMax.Y - 0.1f)
                        );
                    }
                }
                    
                if (oldPos != player.Position)
                {
                    _logger.LogDebug("Player {PlayerId} moved from {OldPos} to {NewPos}", 
                        playerId, oldPos, player.Position);
                    
                    // Check if player is approaching zone boundary
                    var (boundMin, boundMax) = _assignedSquare.GetBounds();
                    var distToEdge = Math.Min(
                        Math.Min(player.Position.X - boundMin.X, boundMax.X - player.Position.X),
                        Math.Min(player.Position.Y - boundMin.Y, boundMax.Y - player.Position.Y)
                    );
                    
                    if (distToEdge < 50)
                    {
                        _logger.LogDebug("[ZONE_TRANSITION_SERVER] Player {PlayerId} is near zone boundary (distance: {Distance}) at position {Position} in zone ({ZoneX},{ZoneY})", 
                            playerId, distToEdge, player.Position, _assignedSquare.X, _assignedSquare.Y);
                    }
                }
                
                // Update rotation based on movement
                if (player.Velocity.Length() > 0)
                {
                    player.Rotation = MathF.Atan2(player.Velocity.Y, player.Velocity.X);
                }
                
                // Handle shooting
                if (input.IsShooting && DateTime.UtcNow - input.LastShot > TimeSpan.FromMilliseconds(250))
                {
                    Vector2 shootDirection;
                    
                    // Use explicit shoot direction if provided
                    if (input.ShootDirection.HasValue)
                    {
                        shootDirection = input.ShootDirection.Value.Normalized();
                    }
                    // Otherwise fall back to movement direction or current rotation
                    else if (input.MoveDirection.Length() > 0)
                    {
                        shootDirection = input.MoveDirection.Normalized();
                    }
                    else
                    {
                        shootDirection = new Vector2(MathF.Cos(player.Rotation), MathF.Sin(player.Rotation));
                    }
                    
                    SpawnBullet(player.Position, shootDirection, false, playerId, player.Team);
                    input.LastShot = DateTime.UtcNow;
                }
            }
        }
        
        // Update all entities
        var (min, max) = _assignedSquare.GetBounds();
        const float boundaryMargin = 5f; // Small margin to prevent entities from sitting exactly on boundaries
        
        foreach (var entity in _entities.Values)
        {
            if (entity.Type != EntityType.Player && entity.State == EntityStateType.Active)
            {
                entity.Position += entity.Velocity * deltaTime;
                
                // Update rotation for moving entities
                if (entity.Velocity.Length() > 0)
                {
                    entity.Rotation = MathF.Atan2(entity.Velocity.Y, entity.Velocity.X);
                }
                
                // Update bullet lifetime (health represents remaining lifetime)
                if (entity.Type == EntityType.Bullet)
                {
                    entity.Health -= deltaTime;
                    entity.StateTimer += deltaTime;

                    // Immediately remove bullets that exit our zone to prevent dual-simulation
                    var bulletZone = GridSquare.FromPosition(entity.Position);
                    if (bulletZone != _assignedSquare)
                    {
                        // Mark bullet for immediate removal - it will be simulated by the target zone
                        entity.Health = 0;
                        // Track this bullet as handed off so we don't re-accept it via trajectory
                        _handedOffBullets[entity.EntityId] = DateTime.UtcNow;
                        _logger.LogDebug("[BULLET_ZONE_EXIT] Bullet {Id} exited zone {OurZone} to {NewZone}, marking for removal and handoff",
                            entity.EntityId, _assignedSquare, bulletZone);
                    }
                }
                
                // Enforce zone boundaries for enemies (bullets will be removed in zone monitoring)
                if (entity.Type == EntityType.Enemy)
                {
                    // Clamp enemies to zone bounds with margin
                    entity.Position = new Vector2(
                        Math.Clamp(entity.Position.X, min.X + boundaryMargin, max.X - boundaryMargin),
                        Math.Clamp(entity.Position.Y, min.Y + boundaryMargin, max.Y - boundaryMargin)
                    );
                    
                    // If enemy hit boundary, reverse velocity on that axis
                    if (entity.Position.X <= min.X + boundaryMargin || entity.Position.X >= max.X - boundaryMargin)
                    {
                        entity.Velocity = new Vector2(-entity.Velocity.X, entity.Velocity.Y);
                    }
                    if (entity.Position.Y <= min.Y + boundaryMargin || entity.Position.Y >= max.Y - boundaryMargin)
                    {
                        entity.Velocity = new Vector2(entity.Velocity.X, -entity.Velocity.Y);
                    }
                }
                
                // Handle asteroids - remove them if they move to non-existent zones
                if (entity.Type == EntityType.Asteroid && (AsteroidSubType)entity.SubType == AsteroidSubType.Moving)
                {
                    var currentZone = GridSquare.FromPosition(entity.Position);
                    if (currentZone != _assignedSquare)
                    {
                        // Check if the new zone exists
                        var zoneExists = await IsZoneAvailable(currentZone);
                        if (!zoneExists)
                        {
                            // Mark asteroid for removal
                            entity.Health = 0;
                            _logger.LogDebug("Asteroid {Id} moved to non-existent zone {Zone}, removing", entity.EntityId, currentZone);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Checks pending bullet trajectories and activates any bullets that have entered our zone.
    /// </summary>
    private void ActivatePendingBullets()
    {
        // First, clean up old handed-off bullet entries (older than 5 seconds)
        var handoffExpiry = DateTime.UtcNow.AddSeconds(-5);
        var expiredHandoffs = _handedOffBullets.Where(kvp => kvp.Value < handoffExpiry).Select(kvp => kvp.Key).ToList();
        foreach (var bulletId in expiredHandoffs)
        {
            _handedOffBullets.TryRemove(bulletId, out _);
        }

        if (_pendingBullets.IsEmpty)
            return;

        var currentTime = GetCurrentGameTime();
        var bulletsToRemove = new List<string>();

        foreach (var (bulletId, pending) in _pendingBullets)
        {
            // Skip bullets we've already handed off
            if (_handedOffBullets.ContainsKey(bulletId))
            {
                bulletsToRemove.Add(bulletId);
                continue;
            }

            var elapsedTime = currentTime - pending.SpawnTime;

            // Check if bullet has expired
            if (elapsedTime >= pending.Lifespan)
            {
                bulletsToRemove.Add(bulletId);
                continue;
            }

            // Calculate current position
            var currentPosition = pending.Origin + pending.Velocity * elapsedTime;
            var currentZone = GridSquare.FromPosition(currentPosition);

            // If bullet is now in our zone, activate it
            if (currentZone == _assignedSquare)
            {
                // Don't activate if we already have this bullet (e.g., from originating in this zone)
                if (!_entities.ContainsKey(bulletId))
                {
                    var bullet = new SimulatedEntity
                    {
                        EntityId = bulletId,
                        Type = EntityType.Bullet,
                        SubType = pending.SubType,
                        Position = currentPosition,
                        Velocity = pending.Velocity,
                        Health = pending.Lifespan - elapsedTime,
                        Rotation = MathF.Atan2(pending.Velocity.Y, pending.Velocity.X),
                        State = EntityStateType.Active,
                        StateTimer = elapsedTime,
                        OwnerId = pending.OwnerId,
                        Team = pending.Team
                    };

                    _entities[bulletId] = bullet;
                    _logger.LogDebug("[PENDING_BULLET] Activated bullet {BulletId} at position {Position} in zone {Zone}",
                        bulletId, currentPosition, _assignedSquare);
                }
                bulletsToRemove.Add(bulletId);
            }
        }

        // Clean up processed pending bullets
        foreach (var bulletId in bulletsToRemove)
        {
            _pendingBullets.TryRemove(bulletId, out _);
        }
    }

    private void UpdateAI(float deltaTime)
    {
        var players = _entities.Values.Where(e => e.Type == EntityType.Player && e.State == EntityStateType.Active).ToList();
        
        foreach (var enemy in _entities.Values.Where(e => e.Type == EntityType.Enemy && (e.State == EntityStateType.Active || e.State == EntityStateType.Alerting)))
        {
            // Special handling for Scouts when no players are present
            if (!players.Any())
            {
                if ((EnemySubType)enemy.SubType == EnemySubType.Scout)
                {
                    // Reset Scout state when all players leave
                    enemy.HasSpottedPlayer = false;
                    enemy.HasAlerted = false;
                    enemy.State = EntityStateType.Active;
                    enemy.StateTimer = 0f;
                    enemy.Velocity = Vector2.Zero;
                    _logger.LogDebug("Scout {EntityId} reset - no players in zone", enemy.EntityId);
                }
                continue;
            }
            
            // Find closest player
            var closestPlayer = players.OrderBy(p => p.Position.DistanceTo(enemy.Position)).First();
            var direction = (closestPlayer.Position - enemy.Position).Normalized();
            var distance = enemy.Position.DistanceTo(closestPlayer.Position);
            
            // Check if enemy is alerted (from Scout alert)
            if (enemy.IsAlerted && enemy.AlertedUntil > DateTime.UtcNow)
            {
                // If there's a player in the current zone, ignore the alert and attack them instead
                if (players.Any())
                {
                    enemy.IsAlerted = false; // Cancel alert, focus on local player
                    _logger.LogDebug("Enemy {EntityId} ignoring alert - player present in zone", enemy.EntityId);
                }
                else
                {
                    // No players in current zone, follow the alert
                    // Move toward last known player position
                    var targetDirection = (enemy.LastKnownPlayerPosition - enemy.Position).Normalized();
                    enemy.Velocity = targetDirection * 19.2f; // Move at moderate speed (reduced by 40% total)
                    
                    // If close to last known position, stop being alerted
                    if (enemy.Position.DistanceTo(enemy.LastKnownPlayerPosition) < 50f)
                    {
                        enemy.IsAlerted = false;
                    }
                    
                    continue; // Skip normal AI behavior while following alert
                }
            }
            else
            {
                enemy.IsAlerted = false; // Clear alert if expired
            }
            
            switch ((EnemySubType)enemy.SubType)
            {
                case EnemySubType.Kamikaze:
                    // Always move towards player at high speed
                    enemy.Velocity = direction * 36f; // Reduced by 20% from 45f
                    break;
                    
                case EnemySubType.Sniper:
                    // Move to range then stop and shoot
                    if (distance > 250f)
                    {
                        enemy.Velocity = direction * 19.2f; // Reduced by 20% from 24f
                    }
                    else
                    {
                        enemy.Velocity = Vector2.Zero; // Stop and shoot
                        if (_random.NextDouble() < 0.04) // Higher chance to shoot
                        {
                            SpawnBullet(enemy.Position, direction, isEnemyBullet: true);
                        }
                    }
                    break;
                    
                case EnemySubType.Strafing:
                    // Move to range then strafe while shooting
                    if (distance > 200f)
                    {
                        enemy.Velocity = direction * 24f; // Reduced by 20% from 30f
                    }
                    else
                    {
                        // Strafe perpendicular to player
                        var strafeDirection = new Vector2(-direction.Y, direction.X);
                        enemy.StrafeDirection ??= _random.NextDouble() > 0.5 ? 1f : -1f;
                        
                        // Change strafe direction occasionally
                        if (_random.NextDouble() < 0.02)
                        {
                            enemy.StrafeDirection *= -1f;
                        }
                        
                        enemy.Velocity = strafeDirection * enemy.StrafeDirection.Value * 28.8f; // Reduced by 20% from 36f
                        
                        // Shoot while strafing
                        if (_random.NextDouble() < 0.03)
                        {
                            SpawnBullet(enemy.Position, direction, isEnemyBullet: true);
                        }
                    }
                    break;
                    
                case EnemySubType.Scout:
                    // Scout behavior: roam randomly, spot player, wait 5s, then alert other zones
                    const float scoutDetectionRange = 300f;
                    const float scoutSpeed = 20f;
                    
                    // If alerting, don't move
                    if (enemy.State == EntityStateType.Alerting)
                    {
                        enemy.Velocity = Vector2.Zero;
                        break;
                    }
                    
                    if (distance <= scoutDetectionRange)
                    {
                        // Player spotted!
                        if (!enemy.HasSpottedPlayer)
                        {
                            enemy.HasSpottedPlayer = true;
                            enemy.StateTimer = 0f; // Start 5-second timer
                            enemy.Velocity = Vector2.Zero; // Stop moving
                            _logger.LogInformation("Scout {EntityId} spotted player {PlayerId} at distance {Distance}", 
                                enemy.EntityId, closestPlayer.EntityId, distance);
                        }
                        
                        if (enemy.HasSpottedPlayer && enemy.StateTimer >= 5f && !enemy.HasAlerted)
                        {
                            // 5 seconds have passed, time to alert other zones
                            enemy.HasAlerted = true;
                            enemy.State = EntityStateType.Alerting;
                            enemy.StateTimer = 0f; // Reset timer for alerting duration
                            
                            // Fire and forget the alert (don't await to avoid blocking update loop)
                            _ = Task.Run(async () => {
                                try 
                                {
                                    var alertDirection = await AlertNeighboringZones(enemy, closestPlayer.Position);
                                    if (alertDirection != -999f) // -999f means no zones to alert
                                    {
                                        enemy.Rotation = alertDirection; // Store alert direction for client
                                    }
                                    else
                                    {
                                        // No zones to alert - scout should not be in alert state
                                        enemy.HasAlerted = false;
                                        enemy.State = EntityStateType.Active;
                                        _logger.LogDebug("Scout {EntityId} has no neighboring zones to alert, returning to roaming", enemy.EntityId);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Failed to process scout alert for {EntityId}", enemy.EntityId);
                                }
                            });
                        }
                        
                        // Continue alerting for up to 2 minutes (120 seconds)
                        if (enemy.State == EntityStateType.Alerting && enemy.StateTimer >= 120f)
                        {
                            // Done alerting, resume normal movement
                            enemy.State = EntityStateType.Active;
                            enemy.StateTimer = 0f;
                            enemy.HasSpottedPlayer = false;
                            enemy.HasAlerted = false;
                            _logger.LogInformation("Scout {EntityId} finished alerting after 2 minutes, resuming movement", enemy.EntityId);
                        }
                    }
                    else
                    {
                        // Player not in range, roam randomly
                        enemy.HasSpottedPlayer = false;
                        enemy.HasAlerted = false;
                        enemy.StateTimer = 0f;
                        enemy.State = EntityStateType.Active; // Reset to active state
                        
                        // Check current grid position within zone
                        var (min, max) = _assignedSquare.GetBounds();
                        var relativeX = (enemy.Position.X - min.X) / (max.X - min.X);
                        var relativeY = (enemy.Position.Y - min.Y) / (max.Y - min.Y);
                        var gridX = (int)(relativeX * 3);
                        var gridY = (int)(relativeY * 3);
                        gridX = Math.Clamp(gridX, 0, 2);
                        gridY = Math.Clamp(gridY, 0, 2);
                        
                        bool needNewDirection = enemy.RoamDirection == Vector2.Zero || _random.NextDouble() < 0.01;
                        
                        // If in center position (1,1), always move toward an edge
                        if (gridX == 1 && gridY == 1)
                        {
                            needNewDirection = true;
                        }
                        
                        // Change roaming direction if needed
                        if (needNewDirection)
                        {
                            // For simplicity in sync context, we'll use basic logic without zone checking
                            // If in center, move toward edge
                            if (gridX == 1 && gridY == 1)
                            {
                                var directions = new[]
                                {
                                    new Vector2(-1, -1), new Vector2(0, -1), new Vector2(1, -1),
                                    new Vector2(-1, 0),                      new Vector2(1, 0),
                                    new Vector2(-1, 1),  new Vector2(0, 1),  new Vector2(1, 1)
                                };
                                enemy.RoamDirection = directions[_random.Next(directions.Length)].Normalized();
                            }
                            else
                            {
                                // Random direction
                                var angle = _random.NextSingle() * MathF.PI * 2;
                                enemy.RoamDirection = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                            }
                        }
                        
                        // Move in roaming direction, but stay within zone bounds
                        var proposedPosition = enemy.Position + enemy.RoamDirection * scoutSpeed * deltaTime;
                        
                        // Check if proposed position is within zone bounds
                        if (proposedPosition.X >= min.X + 50 && proposedPosition.X <= max.X - 50 &&
                            proposedPosition.Y >= min.Y + 50 && proposedPosition.Y <= max.Y - 50)
                        {
                            enemy.Velocity = enemy.RoamDirection * scoutSpeed;
                        }
                        else
                        {
                            // Hit boundary, choose new direction away from boundary
                            enemy.RoamDirection = (enemy.Position - proposedPosition).Normalized();
                            enemy.Velocity = enemy.RoamDirection * scoutSpeed;
                        }
                    }
                    break;
            }
        }
    }

    private void CheckCollisions()
    {
        var entities = _entities.Values.ToList();
        
        for (int i = 0; i < entities.Count; i++)
        {
            for (int j = i + 1; j < entities.Count; j++)
            {
                var e1 = entities[i];
                var e2 = entities[j];
                
                // Skip dead/dying entities (but include Alerting state for Scouts)
                bool e1Valid = e1.State == EntityStateType.Active || e1.State == EntityStateType.Alerting;
                bool e2Valid = e2.State == EntityStateType.Active || e2.State == EntityStateType.Alerting;
                if (!e1Valid || !e2Valid)
                    continue;
                
                var distance = e1.Position.DistanceTo(e2.Position);
                var collisionRadius = 20f; // Simple circular collision
                
                if (distance < collisionRadius)
                {
                    // Handle collision based on entity types
                    if (e1.Type == EntityType.Bullet && e2.Type != EntityType.Bullet)
                    {
                        // Check if bullet should collide with target
                        bool shouldCollide = true;
                        if (e2.Type == EntityType.Player && e1.Team > 0 && e1.Team == e2.Team)
                        {
                            // Don't collide with teammates
                            shouldCollide = false;
                        }
                        
                        if (shouldCollide)
                        {
                            var wasAlive = e2.Health > 0;
                            var damage = 25f;
                            e2.Health -= damage;
                            e1.Health = 0; // Destroy bullet
                            
                            // Record damage event
                            RecordDamageEvent(e1, e2, damage);
                            
                            // Notify neighbor zones about bullet destruction
                            _ = Task.Run(async () => await NotifyNeighborZonesBulletDestroyed(e1.EntityId));
                            
                            // If this killed an enemy, give health to the player who shot the bullet
                            if (wasAlive && e2.Health <= 0 && e2.Type == EntityType.Enemy && e1.SubType == 0) // Player bullet
                            {
                                // Find the player who owns this bullet (bullets have player ID in entity ID)
                                var playerId = e1.OwnerId ?? "";
                                if (!string.IsNullOrEmpty(playerId) && _entities.TryGetValue(playerId, out var player))
                                {
                                    player.Health = Math.Min(player.Health + 2f, 1000f); // Cap at max health
                                    _logger.LogDebug("Player {PlayerId} gained 2 HP for killing enemy, new health: {Health}", playerId, player.Health);
                                }
                            }
                            // If this killed an asteroid, give 5 HP to the player
                            else if (wasAlive && e2.Health <= 0 && e2.Type == EntityType.Asteroid && e1.SubType == 0) // Player bullet
                            {
                                var playerId = e1.OwnerId ?? "";
                                if (!string.IsNullOrEmpty(playerId) && _entities.TryGetValue(playerId, out var player))
                                {
                                    player.Health = Math.Min(player.Health + 5f, 1000f); // 5 HP for asteroids
                                    _logger.LogDebug("Player {PlayerId} gained 5 HP for destroying asteroid, new health: {Health}", playerId, player.Health);
                                }
                            }
                        }
                    }
                    else if (e2.Type == EntityType.Bullet && e1.Type != EntityType.Bullet)
                    {
                        // Check if bullet should collide with target
                        bool shouldCollide = true;
                        if (e1.Type == EntityType.Player && e2.Team > 0 && e2.Team == e1.Team)
                        {
                            // Don't collide with teammates
                            shouldCollide = false;
                        }
                        
                        if (shouldCollide)
                        {
                            var wasAlive = e1.Health > 0;
                            var damage = 25f;
                            e1.Health -= damage;
                            e2.Health = 0; // Destroy bullet
                            
                            // Record damage event
                            RecordDamageEvent(e2, e1, damage);
                            
                            // Notify neighbor zones about bullet destruction
                            _ = Task.Run(async () => await NotifyNeighborZonesBulletDestroyed(e2.EntityId));
                            
                            // If this killed an enemy, give health to the player who shot the bullet
                            if (wasAlive && e1.Health <= 0 && e1.Type == EntityType.Enemy && e2.SubType == 0) // Player bullet
                            {
                                // Find the player who owns this bullet
                                var playerId = e2.OwnerId ?? "";
                                if (!string.IsNullOrEmpty(playerId) && _entities.TryGetValue(playerId, out var player))
                                {
                                    player.Health = Math.Min(player.Health + 2f, 1000f); // Cap at max health
                                    _logger.LogDebug("Player {PlayerId} gained 2 HP for killing enemy, new health: {Health}", playerId, player.Health);
                                }
                            }
                            // If this killed an asteroid, give 5 HP to the player
                            else if (wasAlive && e1.Health <= 0 && e1.Type == EntityType.Asteroid && e2.SubType == 0) // Player bullet
                            {
                                var playerId = e2.OwnerId ?? "";
                                if (!string.IsNullOrEmpty(playerId) && _entities.TryGetValue(playerId, out var player))
                                {
                                    player.Health = Math.Min(player.Health + 5f, 1000f); // 5 HP for asteroids
                                    _logger.LogDebug("Player {PlayerId} gained 5 HP for destroying asteroid, new health: {Health}", playerId, player.Health);
                                }
                            }
                        }
                    }
                    else if (e1.Type == EntityType.Player && e2.Type == EntityType.Enemy)
                    {
                        // Kamikaze enemies do more damage
                        var damage = (EnemySubType)e2.SubType == EnemySubType.Kamikaze ? 30f : 10f;
                        e1.Health -= damage;
                        e2.Health -= 10f;
                        
                        // Record damage events
                        RecordDamageEvent(e2, e1, damage, "collision"); // Enemy damages player
                        RecordDamageEvent(e1, e2, 10f, "collision"); // Player damages enemy
                    }
                    else if (e2.Type == EntityType.Player && e1.Type == EntityType.Enemy)
                    {
                        var damage = (EnemySubType)e1.SubType == EnemySubType.Kamikaze ? 30f : 10f;
                        e2.Health -= damage;
                        e1.Health -= 10f;
                        
                        // Record damage events
                        RecordDamageEvent(e1, e2, damage, "collision"); // Enemy damages player
                        RecordDamageEvent(e2, e1, 10f, "collision"); // Player damages enemy
                    }
                    else if (e1.Type == EntityType.Player && e2.Type == EntityType.Asteroid)
                    {
                        // Colliding with asteroids damages the player
                        e1.Health -= 20f;
                        e2.Health -= 25f; // Damage the asteroid too
                        
                        // Record damage events
                        RecordDamageEvent(e2, e1, 20f, "collision"); // Asteroid damages player
                        RecordDamageEvent(e1, e2, 25f, "collision"); // Player damages asteroid
                    }
                    else if (e2.Type == EntityType.Player && e1.Type == EntityType.Asteroid)
                    {
                        // Colliding with asteroids damages the player
                        e2.Health -= 20f;
                        e1.Health -= 25f; // Damage the asteroid too
                        
                        // Record damage events
                        RecordDamageEvent(e1, e2, 20f, "collision"); // Asteroid damages player
                        RecordDamageEvent(e2, e1, 25f, "collision"); // Player damages asteroid
                    }
                }
            }
        }
    }

    private void UpdateEntityStates(float deltaTime)
    {
        foreach (var entity in _entities.Values)
        {
            // Update state timers
            entity.StateTimer += deltaTime;
            
            // Handle player death and respawn
            if (entity.Type == EntityType.Player)
            {
                switch (entity.State)
                {
                    case EntityStateType.Active:
                        if (entity.Health <= 0)
                        {
                            entity.State = EntityStateType.Dying;
                            entity.StateTimer = 0f;
                            SpawnExplosion(entity.Position);
                            _logger.LogInformation("Player {PlayerId} died", entity.EntityId);
                        }
                        break;
                        
                    case EntityStateType.Dying:
                        if (entity.StateTimer >= 0.5f) // Death animation time
                        {
                            entity.State = EntityStateType.Dead;
                            entity.StateTimer = 0f;
                        }
                        break;
                        
                    case EntityStateType.Dead:
                        if (entity.StateTimer >= 5f) // 5 second respawn delay
                        {
                            entity.State = EntityStateType.Respawning;
                            entity.StateTimer = 0f;
                            
                            // Increment respawn count
                            entity.RespawnCount++;
                            _playerRespawnCounts.AddOrUpdate(entity.EntityId, entity.RespawnCount, (_, _) => entity.RespawnCount);
                            
                            // Respawn at random location within the assigned zone
                            var (min, max) = _assignedSquare.GetBounds();
                            var respawnX = min.X + _random.NextSingle() * (max.X - min.X);
                            var respawnY = min.Y + _random.NextSingle() * (max.Y - min.Y);
                            entity.Position = new Vector2(respawnX, respawnY);
                            entity.Health = 1000f;
                            entity.Velocity = Vector2.Zero;
                            
                            _logger.LogInformation("Player {PlayerId} respawning at position {Position} in zone {Zone} (respawn #{RespawnCount})", 
                                entity.EntityId, entity.Position, _assignedSquare, entity.RespawnCount);
                                
                            // Update player position in Orleans to ensure consistency
                            _ = Task.Run(async () => {
                                try
                                {
                                    var playerGrain = _orleansClient.GetGrain<IPlayerGrain>(entity.EntityId);
                                    await playerGrain.UpdatePosition(entity.Position, entity.Velocity);
                                    
                                    // Also update in WorldManager
                                    var worldManager = _orleansClient.GetGrain<IWorldManagerGrain>(0);
                                    await worldManager.UpdatePlayerPositionAndVelocity(entity.EntityId, entity.Position, entity.Velocity);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Failed to update player position during respawn");
                                }
                            });
                        }
                        break;
                        
                    case EntityStateType.Respawning:
                        if (entity.StateTimer >= 0.5f) // Brief invulnerability
                        {
                            entity.State = EntityStateType.Active;
                            entity.StateTimer = 0f;
                        }
                        break;
                }
            }
            // Handle enemy death
            else if (entity.Type == EntityType.Enemy && entity.State == EntityStateType.Active && entity.Health <= 0)
            {
                entity.State = EntityStateType.Dying;
                SpawnExplosion(entity.Position, isSmall: true);
            }
            // Handle asteroid destruction
            else if (entity.Type == EntityType.Asteroid && entity.State == EntityStateType.Active && entity.Health <= 0)
            {
                entity.State = EntityStateType.Dying;
                SpawnExplosion(entity.Position, isSmall: false); // Bigger explosion for asteroids
            }
            // Handle factory destruction
            else if (entity.Type == EntityType.Factory && entity.State == EntityStateType.Active && entity.Health <= 0)
            {
                entity.State = EntityStateType.Dying;
                SpawnExplosion(entity.Position, isSmall: false);
            }
            // Handle explosion lifetime
            else if (entity.Type == EntityType.Explosion && entity.StateTimer >= 0.5f)
            {
                entity.Health = 0; // Mark for cleanup
            }
        }
    }

    private void RecordDamageEvent(SimulatedEntity attacker, SimulatedEntity target, float damage, string weaponType = "gun")
    {
        var damageEvent = new DamageEvent(
            attacker.EntityId,
            target.EntityId,
            attacker.Type,
            target.Type,
            attacker.SubType,
            target.SubType,
            damage,
            weaponType,
            DateTime.UtcNow
        );
        
        lock (_damageEventsLock)
        {
            _damageEvents.Add(damageEvent);
        }
    }

    private void CleanupDeadEntities()
    {
        // Clean up dead non-player entities
        var deadEntities = _entities.Where(kvp => 
            kvp.Value.Health <= 0 && 
            kvp.Value.Type != EntityType.Player && 
            kvp.Value.State != EntityStateType.Respawning).ToList();
            
        foreach (var (id, entity) in deadEntities)
        {
            _entities.TryRemove(id, out _);
        }
        
        // Clean up dead player entities that have been dead for too long (likely bots that disconnected)
        var deadPlayers = _entities.Where(kvp =>
            kvp.Value.Type == EntityType.Player &&
            kvp.Value.State == EntityStateType.Dead &&
            kvp.Value.StateTimer > 30f).ToList(); // Dead for more than 30 seconds
            
        foreach (var (id, entity) in deadPlayers)
        {
            _logger.LogWarning("[CLEANUP] Removing long-dead player {PlayerId} ({PlayerName}) - dead for {DeadTime:F1} seconds", 
                id, entity.PlayerName, entity.StateTimer);
            _entities.TryRemove(id, out _);
            _playerInputs.TryRemove(id, out _);
        }
        
        // Also clean up disconnected players (those with no recent input)
        var now = DateTime.UtcNow;
        var disconnectedPlayers = _playerInputs
            .Where(p => (now - p.Value.LastUpdated).TotalSeconds > 30) // No input for 30 seconds
            .Select(p => p.Key)
            .ToList();
            
        foreach (var playerId in disconnectedPlayers)
        {
            if (_entities.TryGetValue(playerId, out var entity))
            {
                _logger.LogWarning("Removing disconnected player {PlayerId} ({PlayerName}) - no input for 30+ seconds", 
                    playerId, entity.PlayerName);
                RemovePlayer(playerId);
                
                // Notify GameService about the timeout removal so it can update telemetry
                _onPlayerTimeoutRemoved?.Invoke(playerId);
            }
        }
    }

    private void SpawnFactories(int count)
    {
        var (min, max) = _assignedSquare.GetBounds();
        const float spawnMargin = 50f; // Keep factories away from boundaries
        
        _logger.LogDebug("Spawning {Count} factories in zone {Zone}", count, _assignedSquare);
        
        for (int i = 0; i < count; i++)
        {
            var position = new Vector2(
                _random.NextSingle() * (max.X - min.X - 2 * spawnMargin) + min.X + spawnMargin,
                _random.NextSingle() * (max.Y - min.Y - 2 * spawnMargin) + min.Y + spawnMargin);
                
            // Verify position is in correct zone
            var factoryZone = GridSquare.FromPosition(position);
            if (factoryZone != _assignedSquare)
            {
                _logger.LogError("CRITICAL: Factory spawn position {Position} is in zone {FactoryZone}, not assigned zone {AssignedZone}!", 
                    position, factoryZone, _assignedSquare);
            }
            
            var factory = new SimulatedEntity
            {
                EntityId = $"factory_{_assignedSquare.X}_{_assignedSquare.Y}_{_nextEntityId++}",
                Type = EntityType.Factory,
                Position = position,
                Velocity = Vector2.Zero,
                Health = 500f,
                Rotation = 0,
                State = EntityStateType.Active,
                StateTimer = 0f
            };
            
            _entities[factory.EntityId] = factory;
            _logger.LogDebug("Spawned factory {Id} at position {Position}", factory.EntityId, position);
        }
    }

    private void SpawnAsteroids()
    {
        var (min, max) = _assignedSquare.GetBounds();
        const float borderOffset = 100f; // Within 100 units of border
        
        _logger.LogDebug("Spawning asteroids near borders in zone {Zone}", _assignedSquare);
        
        // Spawn 4 asteroids, one near each border
        var borderPositions = new[]
        {
            // Top border
            new Vector2(
                _random.NextSingle() * (max.X - min.X - 2 * borderOffset) + min.X + borderOffset,
                min.Y + _random.NextSingle() * borderOffset),
            // Bottom border
            new Vector2(
                _random.NextSingle() * (max.X - min.X - 2 * borderOffset) + min.X + borderOffset,
                max.Y - _random.NextSingle() * borderOffset),
            // Left border
            new Vector2(
                min.X + _random.NextSingle() * borderOffset,
                _random.NextSingle() * (max.Y - min.Y - 2 * borderOffset) + min.Y + borderOffset),
            // Right border
            new Vector2(
                max.X - _random.NextSingle() * borderOffset,
                _random.NextSingle() * (max.Y - min.Y - 2 * borderOffset) + min.Y + borderOffset)
        };
        
        for (int i = 0; i < borderPositions.Length; i++)
        {
            var position = borderPositions[i];
            var isMoving = _random.Next(2) == 1;
            var asteroidType = isMoving ? AsteroidSubType.Moving : AsteroidSubType.Stationary;
            
            // Generate random velocity for moving asteroids
            var velocity = Vector2.Zero;
            if (isMoving)
            {
                var angle = _random.NextSingle() * MathF.PI * 2;
                var speed = _random.NextSingle() * 30f + 10f; // 10-40 units/second
                velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed;
            }
            
            var asteroid = new SimulatedEntity
            {
                EntityId = $"asteroid_{_assignedSquare.X}_{_assignedSquare.Y}_{_nextEntityId++}",
                Type = EntityType.Asteroid,
                Position = position,
                Velocity = velocity,
                Health = 50f, // Asteroids have 50 health
                Rotation = _random.NextSingle() * MathF.PI * 2,
                State = EntityStateType.Active,
                StateTimer = 0f,
                SubType = (int)asteroidType
            };
            
            _entities[asteroid.EntityId] = asteroid;
            _logger.LogDebug("Spawned {Type} asteroid {Id} at position {Position}", asteroidType, asteroid.EntityId, position);
        }
    }

    private void SpawnEnemies(int count, EnemySubType enemyType)
    {
        // Get all factories in this zone
        var factories = _entities.Values.Where(e => e.Type == EntityType.Factory && e.State == EntityStateType.Active).ToList();
        
        if (!factories.Any())
        {
            _logger.LogDebug("No factories in zone {Zone}, enemies cannot spawn", _assignedSquare);
            return;
        }
        
        var (min, max) = _assignedSquare.GetBounds();
        const float spawnRadius = 40f; // Spawn enemies near factories
        
        _logger.LogDebug("Spawning {Count} {Type} enemies in zone {Zone} from {FactoryCount} factories", 
            count, enemyType, _assignedSquare, factories.Count);
        
        for (int i = 0; i < count; i++)
        {
            // Pick a random factory to spawn from
            var factory = factories[_random.Next(factories.Count)];
            
            // Spawn enemy near the factory
            var angle = _random.NextSingle() * MathF.PI * 2;
            var distance = _random.NextSingle() * spawnRadius + 20f; // At least 20 units away
            var position = factory.Position + new Vector2(MathF.Cos(angle) * distance, MathF.Sin(angle) * distance);
            
            // Ensure position is within zone bounds
            position = new Vector2(
                Math.Clamp(position.X, min.X + 10f, max.X - 10f),
                Math.Clamp(position.Y, min.Y + 10f, max.Y - 10f));
                
            // Verify position is in correct zone
            var enemyZone = GridSquare.FromPosition(position);
            if (enemyZone != _assignedSquare)
            {
                _logger.LogError("CRITICAL: Enemy spawn position {Position} is in zone {EnemyZone}, not assigned zone {AssignedZone}!", 
                    position, enemyZone, _assignedSquare);
            }
                
            var enemy = new SimulatedEntity
            {
                EntityId = $"enemy_{_assignedSquare.X}_{_assignedSquare.Y}_{_nextEntityId++}",
                Type = EntityType.Enemy,
                SubType = (int)enemyType,
                Position = position,
                Velocity = Vector2.Zero,
                Health = enemyType switch
                {
                    EnemySubType.Kamikaze => 30f,
                    EnemySubType.Scout => 200f, // High HP
                    _ => 50f
                }, // Different health for different enemy types
                Rotation = 0,
                State = EntityStateType.Active,
                StateTimer = 0f
            };
            
            _entities[enemy.EntityId] = enemy;
            _logger.LogDebug("Spawned {Type} enemy {Id} at position {Position}", enemyType, enemy.EntityId, position);
        }
    }

    private void SpawnBullet(Vector2 position, Vector2 direction, bool isEnemyBullet = false, string? ownerId = null, int team = 0)
    {
        // Enemy bullets are 40% of normal speed (60% slower)
        var bulletSpeed = isEnemyBullet ? 200f : 500f;
        const float bulletLifespan = 3f; // Bullets live for 3 seconds
        
        // Calculate spawn position slightly ahead
        var spawnPosition = position + direction * 30f;
        
        // Check if spawn position is outside zone bounds
        var (min, max) = _assignedSquare.GetBounds();
        if (spawnPosition.X < min.X || spawnPosition.X > max.X || 
            spawnPosition.Y < min.Y || spawnPosition.Y > max.Y)
        {
            // If bullet would spawn outside zone, adjust the spawn position to be at the edge
            var adjustedPosition = new Vector2(
                Math.Clamp(spawnPosition.X, min.X + 1f, max.X - 1f),
                Math.Clamp(spawnPosition.Y, min.Y + 1f, max.Y - 1f)
            );
            
            _logger.LogDebug("Bullet spawn position {Original} outside zone, adjusted to {Adjusted}", 
                spawnPosition, adjustedPosition);
            spawnPosition = adjustedPosition;
        }
        
        var bulletId = $"bullet_{_assignedSquare.X}_{_assignedSquare.Y}_{_nextEntityId++}";
        var velocity = direction * bulletSpeed;
        var spawnTime = GetCurrentGameTime();
        
        var bullet = new SimulatedEntity
        {
            EntityId = bulletId,
            Type = EntityType.Bullet,
            Position = spawnPosition,
            Velocity = velocity,
            Health = bulletLifespan, // Use lifespan as health
            Rotation = MathF.Atan2(direction.Y, direction.X),
            State = EntityStateType.Active,
            StateTimer = 0f,
            SubType = isEnemyBullet ? 1 : 0, // Track bullet type
            OwnerId = ownerId, // Track who shot this bullet
            Team = team // Team of the shooter
        };
        
        _entities[bullet.EntityId] = bullet;
        
        // Calculate which zones the bullet might enter based on its trajectory
        _ = Task.Run(async () => await SendBulletTrajectoryToNeighboringZones(
            bulletId, bullet.SubType, spawnPosition, velocity, spawnTime, bulletLifespan, ownerId, bullet.Team));
    }

    private void SpawnExplosion(Vector2 position, bool isSmall = false)
    {
        var explosion = new SimulatedEntity
        {
            EntityId = $"explosion_{_assignedSquare.X}_{_assignedSquare.Y}_{_nextEntityId++}",
            Type = EntityType.Explosion,
            Position = position,
            Velocity = Vector2.Zero,
            Health = 1f,
            Rotation = 0,
            State = EntityStateType.Active,
            StateTimer = 0f,
            SubType = isSmall ? 1 : 0
        };
        
        _entities[explosion.EntityId] = explosion;
    }
    
    private async Task<float> AlertNeighboringZones(SimulatedEntity scout, Vector2 playerPosition)
    {
        try
        {
            // Determine scout's position within the zone using 3x3 grid
            var (min, max) = _assignedSquare.GetBounds();
            var relativeX = (scout.Position.X - min.X) / (max.X - min.X); // 0.0 to 1.0
            var relativeY = (scout.Position.Y - min.Y) / (max.Y - min.Y); // 0.0 to 1.0
            
            // Map to 3x3 grid (0, 1, 2)
            var gridX = (int)(relativeX * 3);
            var gridY = (int)(relativeY * 3);
            gridX = Math.Clamp(gridX, 0, 2);
            gridY = Math.Clamp(gridY, 0, 2);
            
            _logger.LogInformation("Scout {EntityId} alerting from grid position ({GridX},{GridY}) in zone ({ZoneX},{ZoneY})", 
                scout.EntityId, gridX, gridY, _assignedSquare.X, _assignedSquare.Y);
            
            // Determine which zones to alert based on grid position
            var zonesToAlert = new List<GridSquare>();
            float alertDirection = 0f; // Default direction (right)
            
            // Center position (1,1) - alert all 8 neighboring zones
            if (gridX == 1 && gridY == 1)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue; // Skip current zone
                        zonesToAlert.Add(new GridSquare(_assignedSquare.X + dx, _assignedSquare.Y + dy));
                    }
                }
                // Center position - encode all directions (will show 8-directional pattern)
                alertDirection = 0f; // Special value for center - client will show 8-way pattern
            }
            else
            {
                // Border positions - alert in the direction of the border
                var alertX = gridX == 0 ? -1 : (gridX == 2 ? 1 : 0);
                var alertY = gridY == 0 ? -1 : (gridY == 2 ? 1 : 0);
                
                // Add primary direction zone
                if (alertX != 0 || alertY != 0)
                {
                    zonesToAlert.Add(new GridSquare(_assignedSquare.X + alertX, _assignedSquare.Y + alertY));
                }
                
                // Add adjacent zones for corner positions
                if (alertX != 0 && alertY != 0) // Corner position
                {
                    zonesToAlert.Add(new GridSquare(_assignedSquare.X + alertX, _assignedSquare.Y));
                    zonesToAlert.Add(new GridSquare(_assignedSquare.X, _assignedSquare.Y + alertY));
                }
            }
            
            // Send alerts to enemies in target zones
            var worldManager = _orleansClient.GetGrain<IWorldManagerGrain>(0);
            
            // First, check which zones actually exist
            var existingZones = new List<GridSquare>();
            foreach (var zone in zonesToAlert)
            {
                var targetPosition = zone.GetCenter();
                var targetServer = await worldManager.GetActionServerForPosition(targetPosition);
                if (targetServer != null)
                {
                    existingZones.Add(zone);
                }
            }
            
            // Calculate alert direction based only on existing zones
            if (existingZones.Count > 0)
            {
                if (gridX == 1 && gridY == 1)
                {
                    // Center position - only show 8-way pattern if we have zones to alert
                    alertDirection = 0f;
                }
                else
                {
                    // Calculate average direction to existing zones
                    float sumX = 0, sumY = 0;
                    foreach (var zone in existingZones)
                    {
                        var dx = zone.X - _assignedSquare.X;
                        var dy = zone.Y - _assignedSquare.Y;
                        sumX += dx;
                        sumY += dy;
                    }
                    alertDirection = MathF.Atan2(sumY / existingZones.Count, sumX / existingZones.Count);
                }
            }
            else
            {
                // No zones to alert - don't show alert direction
                _logger.LogDebug("Scout at {Position} has no neighboring zones to alert", scout.Position);
                return -999f; // Special value to indicate no alert direction
            }
            
            foreach (var targetZone in zonesToAlert)
            {
                try
                {
                    var targetPosition = targetZone.GetCenter(); // Get center of target zone
                    var targetServer = await worldManager.GetActionServerForPosition(targetPosition);
                    if (targetServer != null)
                    {
                        // Fire and forget - don't await scout alerts
                        _ = SendScoutAlert(targetServer, _assignedSquare, playerPosition, targetZone)
                            .ContinueWith(t => 
                            {
                                if (t.IsFaulted)
                                {
                                    _logger.LogError(t.Exception, "Failed to send scout alert to zone ({X},{Y})", targetZone.X, targetZone.Y);
                                }
                                else
                                {
                                    _logger.LogInformation("Scout alert sent to zone ({X},{Y})", targetZone.X, targetZone.Y);
                                }
                            });
                    }
                    else
                    {
                        _logger.LogDebug("Scout at {Position} cannot alert zone ({X},{Y}) - no action server assigned", 
                            playerPosition, targetZone.X, targetZone.Y);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send scout alert to zone ({X},{Y})", targetZone.X, targetZone.Y);
                }
            }
            
            return alertDirection;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to alert neighboring zones from scout {EntityId}", scout.EntityId);
            return 0f; // Default direction on error
        }
    }
    
    private async Task SendScoutAlert(ActionServerInfo targetServer, GridSquare playerZone, Vector2 playerPosition, GridSquare targetZone)
    {
        try
        {
            _logger.LogDebug("Sending scout alert to server {ServerId} for zone ({TargetX},{TargetY}) about player in zone ({PlayerX},{PlayerY})", 
                targetServer.ServerId, targetZone.X, targetZone.Y, playerZone.X, playerZone.Y);

            // Use the zone-aware method to check if connection should be allowed
            var gameGrain = await _crossZoneRpc.GetGameGrainForZone(targetServer, targetZone);
            
            if (gameGrain == null)
            {
                _logger.LogError("Failed to get game grain for server {ServerId} zone ({X},{Y})", 
                    targetServer.ServerId, targetZone.X, targetZone.Y);
                return;
            }

            _logger.LogDebug("Got game grain for server {ServerId}, sending scout alert", targetServer.ServerId);
            await gameGrain.ReceiveScoutAlert(playerZone, playerPosition);
            
            _logger.LogDebug("Successfully sent scout alert to server {ServerId}", targetServer.ServerId);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not connected"))
        {
            _logger.LogWarning("RPC connection not available for scout alert to server {ServerId}: {Message}", 
                targetServer.ServerId, ex.Message);
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning("Timeout sending scout alert to server {ServerId}: {Message}", 
                targetServer.ServerId, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send RPC scout alert to server {ServerId} for zone ({X},{Y}): {Message}", 
                targetServer.ServerId, targetZone.X, targetZone.Y, ex.Message);
        }
    }

    private class SimulatedEntity
    {
        public required string EntityId { get; set; }
        public EntityType Type { get; set; }
        public int SubType { get; set; }
        public Vector2 Position { get; set; }
        public Vector2 Velocity { get; set; }
        public float Health { get; set; }
        public float Rotation { get; set; }
        public EntityStateType State { get; set; }
        public float StateTimer { get; set; }
        public float? StrafeDirection { get; set; } // For strafing enemies
        public string? OwnerId { get; set; } // For bullets to track who shot them
        public int Team { get; set; } = 0; // 0 = hostile/enemy, 1 = team 1, 2 = team 2
        
        // Scout-specific fields
        public bool HasSpottedPlayer { get; set; }
        public bool HasAlerted { get; set; }
        public Vector2 RoamDirection { get; set; }
        
        // Alert system fields
        public bool IsAlerted { get; set; }
        public DateTime AlertedUntil { get; set; }
        public Vector2 LastKnownPlayerPosition { get; set; }
        public string? PlayerName { get; set; }
        
        // Game stats tracking
        public int RespawnCount { get; set; }
    }

    private class PlayerInput
    {
        public Vector2 MoveDirection { get; set; }
        public Vector2? ShootDirection { get; set; }
        public bool IsShooting { get; set; }
        public DateTime LastUpdated { get; set; }
        public DateTime LastShot { get; set; }
    }
    
    public Task<List<string>> GetPlayersOutsideZone()
    {
        var playersOutside = new List<string>();
        var (min, max) = _assignedSquare.GetBounds();
        
        foreach (var (playerId, entity) in _entities.Where(e => e.Value.Type == EntityType.Player))
        {
            var pos = entity.Position;
            if (pos.X < min.X || pos.X >= max.X || pos.Y < min.Y || pos.Y >= max.Y)
            {
                var actualZone = GridSquare.FromPosition(pos);
                _logger.LogInformation("[ZONE_TRANSITION_SERVER] Player {PlayerId} at position {Position} is outside zone bounds ({MinX},{MinY}) to ({MaxX},{MaxY})", 
                    playerId, pos, min.X, min.Y, max.X, max.Y);
                playersOutside.Add(playerId);
                _logger.LogInformation("[ZONE_TRANSITION_SERVER] Player {PlayerId} is in zone ({ActualX},{ActualY}) but should be in zone ({AssignedX},{AssignedY})", 
                    playerId, actualZone.X, actualZone.Y, _assignedSquare.X, _assignedSquare.Y);
            }
        }
        
        if (playersOutside.Count > 0)
        {
            _logger.LogInformation("GetPlayersOutsideZone returning {Count} players: {Players}", 
                playersOutside.Count, string.Join(", ", playersOutside));
        }
        
        return Task.FromResult(playersOutside);
    }
    
    public Task<List<(string entityId, Vector2 position, EntityType type, int subType)>> GetEntitiesOutsideZone()
    {
        var entitiesOutside = new List<(string, Vector2, EntityType, int)>();
        var (min, max) = _assignedSquare.GetBounds();
        
        foreach (var (entityId, entity) in _entities.Where(e => e.Value.Type == EntityType.Enemy || e.Value.Type == EntityType.Bullet))
        {
            var pos = entity.Position;
            if (pos.X < min.X || pos.X >= max.X || pos.Y < min.Y || pos.Y >= max.Y)
            {
                entitiesOutside.Add((entityId, pos, entity.Type, entity.SubType));
            }
        }
        
        return Task.FromResult(entitiesOutside);
    }
    
    public Task<bool> TransferEntityIn(string entityId, EntityType type, int subType, Vector2 position, Vector2 velocity, float health)
    {
        try
        {
            // Validate that the entity position is actually in our zone
            var entityZone = GridSquare.FromPosition(position);
            if (entityZone != _assignedSquare)
            {
                _logger.LogError("[TRANSFER_IN] REJECTED: {Type} {EntityId} at position {Position} belongs to zone {EntityZone}, not our zone {OurZone}", 
                    type, entityId, position, entityZone, _assignedSquare);
                return Task.FromResult(false);
            }
            
            // Log the incoming transfer details
            _logger.LogInformation("[TRANSFER_IN] Receiving {Type} {EntityId} at position {Position} with velocity {Velocity}, health {Health} into zone {Zone}", 
                type, entityId, position, velocity, health, _assignedSquare);
            
            // Check if entity already exists (possible duplicate transfer)
            if (_entities.ContainsKey(entityId))
            {
                _logger.LogWarning("[TRANSFER_IN] Entity {EntityId} already exists in zone {Zone}, updating position from {OldPos} to {NewPos}", 
                    entityId, _assignedSquare, _entities[entityId].Position, position);
            }
            
            var entity = new SimulatedEntity
            {
                EntityId = entityId,
                Type = type,
                SubType = subType,
                Position = position,
                Velocity = velocity,
                Health = health,
                Rotation = velocity.Length() > 0 ? MathF.Atan2(velocity.Y, velocity.X) : 0,
                State = EntityStateType.Active,
                StateTimer = 0f
            };
            
            _entities[entityId] = entity;
            
            // If it's a player, also add to player inputs and get the player name
            if (type == EntityType.Player)
            {
                _playerInputs[entityId] = new PlayerInput
                {
                    MoveDirection = Vector2.Zero,
                    ShootDirection = null,
                    IsShooting = false,
                    LastUpdated = DateTime.UtcNow
                };
                
                // Get player name from grain
                _ = Task.Run(async () => {
                    try
                    {
                        var playerGrain = _orleansClient.GetGrain<IPlayerGrain>(entityId);
                        var playerInfo = await playerGrain.GetInfo();
                        if (playerInfo != null && !string.IsNullOrEmpty(playerInfo.Name))
                        {
                            entity.PlayerName = playerInfo.Name;
                            
                            // Update SubType based on name pattern if not already set correctly
                            bool isBot = System.Text.RegularExpressions.Regex.IsMatch(
                                playerInfo.Name, 
                                @"^(LiteNetLib|Ruffles)(Test)?\d+$", 
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            entity.SubType = isBot ? 1 : 0;
                            
                            _logger.LogInformation("[TRANSFER_IN] Got player name {PlayerName} for {PlayerId}, isBot: {IsBot}", 
                                playerInfo.Name, entityId, isBot);
                        }
                        
                        // Update player position and health in Orleans grain for consistency
                        await playerGrain.UpdatePosition(position, velocity);
                        await playerGrain.UpdateHealth(health);
                        _logger.LogInformation("[TRANSFER_IN] Updated player grain for {PlayerId} - position: {Position}, health: {Health}", 
                            entityId, position, health);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[TRANSFER_IN] Failed to update player grain for {PlayerId}", entityId);
                    }
                });
                
                _logger.LogInformation("[TRANSFER_IN] Player {EntityId} successfully transferred to zone {Zone} at position {Position} with health {Health}", 
                    entityId, _assignedSquare, position, health);
            }
            else
            {
                _logger.LogInformation("[TRANSFER_IN] {Type} {EntityId} transferred to zone {Zone} at position {Position}", 
                    type, entityId, _assignedSquare, position);
            }
            
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TRANSFER_IN] Failed to transfer in entity {EntityId}", entityId);
            return Task.FromResult(false);
        }
    }
    
    private async Task<bool> IsZoneAvailable(GridSquare zone)
    {
        // Cache available zones for 10 seconds
        if (DateTime.UtcNow - _lastZoneCheck > TimeSpan.FromSeconds(10))
        {
            try
            {
                var worldManager = _orleansClient.GetGrain<IWorldManagerGrain>(0);
                var servers = await worldManager.GetAllActionServers();
                _availableZones = servers.Select(s => s.AssignedSquare).ToHashSet();
                _lastZoneCheck = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch available zones");
            }
        }
        
        // Always allow current zone
        if (zone == _assignedSquare)
            return true;
            
        return _availableZones.Contains(zone);
    }
    
    public ZoneDamageReport GetDamageReport()
    {
        lock (_damageEventsLock)
        {
            // Calculate player stats from damage events
            var playerStats = new Dictionary<string, PlayerDamageStats>();
            
            foreach (var damage in _damageEvents)
            {
                // Track damage dealt
                if (damage.AttackerType == EntityType.Player)
                {
                    if (!playerStats.ContainsKey(damage.AttackerId))
                    {
                        var playerName = _entities.TryGetValue(damage.AttackerId, out var player) ? player.PlayerName ?? damage.AttackerId : damage.AttackerId;
                        playerStats[damage.AttackerId] = new PlayerDamageStats(
                            damage.AttackerId,
                            playerName,
                            new Dictionary<string, float>(),
                            new Dictionary<string, float>(),
                            new Dictionary<string, float>(),
                            0,
                            0
                        );
                    }
                    
                    var stats = playerStats[damage.AttackerId];
                    
                    // Track damage by weapon type
                    if (!stats.DamageDealtByWeapon.ContainsKey(damage.WeaponType))
                        stats.DamageDealtByWeapon[damage.WeaponType] = 0;
                    stats.DamageDealtByWeapon[damage.WeaponType] += damage.Damage;
                    
                    // Update total damage dealt
                    playerStats[damage.AttackerId] = stats with { 
                        TotalDamageDealt = stats.TotalDamageDealt + damage.Damage 
                    };
                }
                
                // Track damage received
                if (damage.TargetType == EntityType.Player)
                {
                    if (!playerStats.ContainsKey(damage.TargetId))
                    {
                        var playerName = _entities.TryGetValue(damage.TargetId, out var player) ? player.PlayerName ?? damage.TargetId : damage.TargetId;
                        playerStats[damage.TargetId] = new PlayerDamageStats(
                            damage.TargetId,
                            playerName,
                            new Dictionary<string, float>(),
                            new Dictionary<string, float>(),
                            new Dictionary<string, float>(),
                            0,
                            0
                        );
                    }
                    
                    var stats = playerStats[damage.TargetId];
                    
                    // Track damage by enemy type
                    if (damage.AttackerType == EntityType.Enemy)
                    {
                        var enemyTypeName = GetEnemyTypeName(damage.AttackerSubType);
                        if (!stats.DamageReceivedByEnemyType.ContainsKey(enemyTypeName))
                            stats.DamageReceivedByEnemyType[enemyTypeName] = 0;
                        stats.DamageReceivedByEnemyType[enemyTypeName] += damage.Damage;
                    }
                    
                    // Track damage by weapon type
                    if (!stats.DamageReceivedByWeapon.ContainsKey(damage.WeaponType))
                        stats.DamageReceivedByWeapon[damage.WeaponType] = 0;
                    stats.DamageReceivedByWeapon[damage.WeaponType] += damage.Damage;
                    
                    // Update total damage received
                    playerStats[damage.TargetId] = stats with { 
                        TotalDamageReceived = stats.TotalDamageReceived + damage.Damage 
                    };
                }
            }
            
            return new ZoneDamageReport(
                _assignedSquare,
                _startTime,
                DateTime.UtcNow,
                new List<DamageEvent>(_damageEvents),
                playerStats
            );
        }
    }
    
    private string GetEnemyTypeName(int subType)
    {
        return subType switch
        {
            1 => "Kamikaze",
            2 => "Sniper",
            3 => "Strafing",
            4 => "Scout",
            _ => "Unknown"
        };
    }

    public PlayerInfo? GetPlayerInfo(string playerId)
    {
        if (_entities.TryGetValue(playerId, out var entity) && entity.Type == EntityType.Player)
        {
            return new PlayerInfo(playerId, entity.PlayerName ?? playerId, entity.Position, entity.Velocity, entity.Health);
        }
        return null;
    }
    
    public void ProcessScoutAlert(GridSquare playerZone, Vector2 playerPosition)
    {
        _logger.LogInformation("Processing scout alert: Player in zone ({X},{Y}) at {Position}", 
            playerZone.X, playerZone.Y, playerPosition);
        
        // Make enemies in this zone more aggressive and move toward the player zone
        var enemies = _entities.Values.Where(e => e.Type == EntityType.Enemy && e.State == EntityStateType.Active);
        
        foreach (var enemy in enemies)
        {
            // Mark enemy as alerted (use StateTimer for alert duration)
            enemy.IsAlerted = true;
            enemy.AlertedUntil = DateTime.UtcNow.AddSeconds(30); // Stay alerted for 30 seconds
            enemy.LastKnownPlayerPosition = playerPosition;
            
            _logger.LogDebug("Enemy {EntityId} of type {SubType} alerted to player presence", 
                enemy.EntityId, (EnemySubType)enemy.SubType);
        }
    }
    
    private async Task<bool> IsAtEdgeNearUnavailableZone(int gridX, int gridY)
    {
        // Check if this edge position is adjacent to an unavailable zone
        if (gridX == 0 || gridX == 2 || gridY == 0 || gridY == 2)
        {
            // Determine which zone(s) to check based on edge position
            var zonesToCheck = new List<GridSquare>();
            
            if (gridX == 0) zonesToCheck.Add(new GridSquare(_assignedSquare.X - 1, _assignedSquare.Y));
            if (gridX == 2) zonesToCheck.Add(new GridSquare(_assignedSquare.X + 1, _assignedSquare.Y));
            if (gridY == 0) zonesToCheck.Add(new GridSquare(_assignedSquare.X, _assignedSquare.Y - 1));
            if (gridY == 2) zonesToCheck.Add(new GridSquare(_assignedSquare.X, _assignedSquare.Y + 1));
            
            // Corner positions check two zones
            if (gridX == 0 && gridY == 0) zonesToCheck.Add(new GridSquare(_assignedSquare.X - 1, _assignedSquare.Y - 1));
            if (gridX == 2 && gridY == 0) zonesToCheck.Add(new GridSquare(_assignedSquare.X + 1, _assignedSquare.Y - 1));
            if (gridX == 0 && gridY == 2) zonesToCheck.Add(new GridSquare(_assignedSquare.X - 1, _assignedSquare.Y + 1));
            if (gridX == 2 && gridY == 2) zonesToCheck.Add(new GridSquare(_assignedSquare.X + 1, _assignedSquare.Y + 1));
            
            foreach (var zone in zonesToCheck)
            {
                if (!await IsZoneAvailable(zone))
                {
                    return true; // At edge near unavailable zone
                }
            }
        }
        
        return false;
    }
    
    private Task<Vector2> GetValidRoamDirection(int gridX, int gridY, bool isAtBadEdge)
    {
        // If in center, move toward a random edge
        if (gridX == 1 && gridY == 1)
        {
            // Pick a random direction away from center
            var directions = new[]
            {
                new Vector2(-1, -1), new Vector2(0, -1), new Vector2(1, -1),
                new Vector2(-1, 0),                      new Vector2(1, 0),
                new Vector2(-1, 1),  new Vector2(0, 1),  new Vector2(1, 1)
            };
            return Task.FromResult(directions[_random.Next(directions.Length)].Normalized());
        }
        
        // If at a bad edge, move toward center or valid edges
        if (isAtBadEdge)
        {
            // Move toward center
            var toCenterX = 1 - gridX;
            var toCenterY = 1 - gridY;
            
            // Add some randomness to avoid predictable paths
            var angle = MathF.Atan2(toCenterY, toCenterX) + (_random.NextSingle() - 0.5f) * MathF.PI * 0.5f;
            return Task.FromResult(new Vector2(MathF.Cos(angle), MathF.Sin(angle)));
        }
        
        // Otherwise, random direction
        var randomAngle = _random.NextSingle() * MathF.PI * 2;
        return Task.FromResult(new Vector2(MathF.Cos(randomAngle), MathF.Sin(randomAngle)));
    }
    
    public void ReceiveBulletTrajectory(string bulletId, int subType, Vector2 origin, Vector2 velocity, float spawnTime, float lifespan, string? ownerId, int team = 0)
    {
        try
        {
            // CRITICAL: Reject bullets we've already handed off to another zone
            // This prevents oscillation when a bullet crosses back into our zone
            if (_handedOffBullets.ContainsKey(bulletId))
            {
                _logger.LogDebug("[BULLET_REJECTED] Bullet {BulletId} was previously handed off, ignoring trajectory", bulletId);
                return;
            }

            // Calculate current position based on elapsed time
            var currentTime = GetCurrentGameTime();
            var elapsedTime = currentTime - spawnTime;

            // If the bullet has already expired, don't spawn it
            if (elapsedTime >= lifespan)
            {
                _logger.LogDebug("Bullet {BulletId} has already expired (elapsed: {Elapsed}s, lifespan: {Lifespan}s)",
                    bulletId, elapsedTime, lifespan);
                return;
            }

            // Calculate current position along trajectory
            var currentPosition = origin + velocity * elapsedTime;

            // IMPORTANT: Only spawn bullet if it's ACTUALLY in our zone right now
            // Do NOT use look-ahead or "recently left" logic - this causes oscillation
            // because both zones will simulate the bullet simultaneously near boundaries
            var currentZone = GridSquare.FromPosition(currentPosition);

            if (currentZone != _assignedSquare)
            {
                // Bullet is not in our zone yet - store the trajectory info for later spawning
                // when the bullet actually enters our zone
                var remainingLifespan = lifespan - elapsedTime;
                if (remainingLifespan > 0)
                {
                    // Check if bullet will ever enter our zone
                    bool willEnter = false;
                    for (float t = 0; t <= remainingLifespan; t += 0.05f)
                    {
                        var futurePosition = origin + velocity * (elapsedTime + t);
                        var futureZone = GridSquare.FromPosition(futurePosition);
                        if (futureZone == _assignedSquare)
                        {
                            willEnter = true;
                            break;
                        }
                    }

                    if (willEnter)
                    {
                        // Store pending bullet trajectory for later activation
                        _pendingBullets[bulletId] = new PendingBulletInfo
                        {
                            BulletId = bulletId,
                            SubType = subType,
                            Origin = origin,
                            Velocity = velocity,
                            SpawnTime = spawnTime,
                            Lifespan = lifespan,
                            OwnerId = ownerId,
                            Team = team
                        };
                        _logger.LogDebug("Bullet {BulletId} not in our zone yet (zone: {Zone}), stored as pending for zone {OurZone}",
                            bulletId, currentZone, _assignedSquare);
                    }
                }
                return;
            }
            
            // Create bullet entity at calculated position
            var bullet = new SimulatedEntity
            {
                EntityId = bulletId,
                Type = EntityType.Bullet,
                SubType = subType,
                Position = currentPosition,
                Velocity = velocity,
                Health = lifespan - elapsedTime, // Use remaining lifespan as health
                Rotation = MathF.Atan2(velocity.Y, velocity.X),
                State = EntityStateType.Active,
                StateTimer = elapsedTime,
                OwnerId = ownerId,
                Team = team
            };
            
            _entities[bulletId] = bullet;
            
            _logger.LogDebug("Spawned transferred bullet {BulletId} at position {Position} with remaining lifespan {Lifespan}s", 
                bulletId, currentPosition, bullet.Health);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to receive bullet trajectory {BulletId}", bulletId);
        }
    }
    
    private float GetCurrentGameTime()
    {
        // Get a consistent game time in seconds since startup
        return (float)(DateTime.UtcNow - _startTime).TotalSeconds;
    }
    
    private async Task SendBulletTrajectoryToNeighboringZones(string bulletId, int subType, Vector2 origin, Vector2 velocity, float spawnTime, float lifespan, string? ownerId, int team = 0)
    {
        try
        {
            // Calculate which zones the bullet might pass through during its lifetime
            var endPosition = origin + velocity * lifespan;
            var zonesToNotify = new HashSet<GridSquare>();
            
            // Sample multiple points along the trajectory
            const int samples = 10;
            for (int i = 0; i <= samples; i++)
            {
                var t = i / (float)samples;
                var samplePos = origin + velocity * (lifespan * t);
                var zone = GridSquare.FromPosition(samplePos);
                
                // Only notify zones that are not our current zone
                if (zone != _assignedSquare)
                {
                    zonesToNotify.Add(zone);
                }
            }
            
            if (zonesToNotify.Count == 0)
            {
                // Bullet stays within our zone
                return;
            }
            
            _logger.LogInformation("Bullet {BulletId} from zone ({FromX},{FromY}) will pass through zones: {Zones}", 
                bulletId, _assignedSquare.X, _assignedSquare.Y, 
                string.Join(", ", zonesToNotify.Select(z => $"({z.X},{z.Y})")));
            
            // Get server information for each zone
            var worldManager = _orleansClient.GetGrain<IWorldManagerGrain>(0);
            
            foreach (var targetZone in zonesToNotify)
            {
                try
                {
                    var targetPosition = targetZone.GetCenter();
                    var targetServer = await worldManager.GetActionServerForPosition(targetPosition);
                    
                    if (targetServer != null && targetServer.RpcPort > 0)
                    {
                        // Send trajectory to this server
                        await SendBulletTrajectoryToServer(targetServer, bulletId, subType, origin, velocity, spawnTime, lifespan, ownerId, team, targetZone);
                        _logger.LogDebug("Sent bullet trajectory {BulletId} to zone ({X},{Y})", 
                            bulletId, targetZone.X, targetZone.Y);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send bullet trajectory to zone ({X},{Y})", 
                        targetZone.X, targetZone.Y);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send bullet trajectory {BulletId} to neighboring zones", bulletId);
        }
    }
    
    private async Task SendBulletTrajectoryToServer(ActionServerInfo targetServer, string bulletId, int subType, 
        Vector2 origin, Vector2 velocity, float spawnTime, float lifespan, string? ownerId, int team = 0, GridSquare? targetZone = null)
    {
        try
        {
            var gameGrain = await _crossZoneRpc.GetGameGrainForZone(targetServer, targetZone, bypassZoneCheck: true);
            // Fire and forget - bullet transfers don't need confirmation
            _ = gameGrain.TransferBulletTrajectory(bulletId, subType, origin, velocity, spawnTime, lifespan, ownerId, team)
                .ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        _logger.LogError(t.Exception, "Failed to send RPC bullet trajectory to server {ServerId}", targetServer.ServerId);
                    }
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get game grain for server {ServerId}", targetServer.ServerId);
        }
    }
    
    private async Task CheckGameOverCondition()
    {
        // Count living enemies and factories
        var enemyCount = _entities.Values.Count(e => 
            (e.Type == EntityType.Enemy || e.Type == EntityType.Factory) && 
            e.State != EntityStateType.Dead && e.State != EntityStateType.Dying);
            
        if (enemyCount == 0 && !_allEnemiesDefeated)
        {
            // Mark that all enemies have been defeated
            _allEnemiesDefeated = true;
            _lastEnemyDeathTime = DateTime.UtcNow;
            _logger.LogInformation("All enemies destroyed! Starting 15 second countdown to game over...");
        }
        else if (enemyCount > 0)
        {
            // Reset if enemies respawn somehow
            _allEnemiesDefeated = false;
        }
        
        // Check if it's time to trigger victory pause (10 seconds after last enemy death)
        if (_allEnemiesDefeated && _currentPhase == GamePhase.Playing && 
            (DateTime.UtcNow - _lastEnemyDeathTime).TotalSeconds >= 10)
        {
            // Now trigger victory pause
            _currentPhase = GamePhase.VictoryPause;
            _victoryPauseTime = DateTime.UtcNow;
            
            _logger.LogInformation("VICTORY PAUSE! Starting 10-second pause before game restart.");
            
            // Collect player scores with mock data
            var playerScores = GeneratePlayerScores();
            
            // Send victory pause message to all players
            var victoryPauseMessage = new VictoryPauseMessage(playerScores, _victoryPauseTime, 10);
            NotifyAllPlayersVictoryPause(victoryPauseMessage);
            
            // Send initial victory chat message with scores
            SendVictoryScoresMessage(playerScores);
            
            // Notify the WorldManager about game over for round tracking
            var worldManager = _orleansClient.GetGrain<IWorldManagerGrain>(0);
            await worldManager.NotifyGameOver();
            
            // Send player scores in chat
            if (playerScores.Any())
            {
                var scoreMessage = "Player Scores:\n" + string.Join("\n", 
                    playerScores.OrderBy(s => s.RespawnCount)
                    .Select((s, i) => $"{i + 1}. {s.PlayerName} - {s.RespawnCount} respawns"));
                
                var scoresChatMessage = new ChatMessage(
                    "System",
                    "Game System",
                    scoreMessage,
                    DateTime.UtcNow,
                    true
                );
                _gameEventBroker.RaiseChatMessage(scoresChatMessage);
            }
        }
    }
    
    private async Task NotifyAllPlayersGameOver(GameOverMessage gameOverMessage)
    {
        try
        {
            // Get all player grains and notify them
            var tasks = new List<Task>();
            foreach (var playerId in _entities.Where(e => e.Value.Type == EntityType.Player).Select(e => e.Key))
            {
                var playerGrain = _orleansClient.GetGrain<IPlayerGrain>(playerId);
                tasks.Add(playerGrain.NotifyGameOver(gameOverMessage));
            }
            
            await Task.WhenAll(tasks);
            
            // Notify RPC clients through GameEventBroker
            _gameEventBroker.RaiseGameOver(gameOverMessage);
            _logger.LogInformation("Game over event raised through GameEventBroker");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to notify players of game over");
        }
    }
    
    private void NotifyAllPlayersVictoryPause(VictoryPauseMessage victoryPauseMessage)
    {
        try
        {
            // Notify RPC clients through GameEventBroker
            _gameEventBroker.RaiseVictoryPause(victoryPauseMessage);
            _logger.LogInformation("Victory pause event raised through GameEventBroker");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to notify players of victory pause");
        }
    }
    
    private async Task RestartGame()
    {
        _logger.LogInformation("Restarting game...");
        _currentPhase = GamePhase.Restarting;
        
        // Clear all entities except players
        var playersToKeep = _entities.Where(e => e.Value.Type == EntityType.Player).ToList();
        _entities.Clear();
        
        // Re-add players with full health and reset respawn counts
        foreach (var (playerId, player) in playersToKeep)
        {
            player.Health = 1000f;
            player.State = EntityStateType.Active;
            player.StateTimer = 0f;
            player.RespawnCount = 0;
            
            // Respawn at random location
            var (min, max) = _assignedSquare.GetBounds();
            var respawnX = min.X + _random.NextSingle() * (max.X - min.X);
            var respawnY = min.Y + _random.NextSingle() * (max.Y - min.Y);
            player.Position = new Vector2(respawnX, respawnY);
            player.Velocity = Vector2.Zero;
            
            _entities.TryAdd(playerId, player);
        }
        
        // Clear respawn counts
        _playerRespawnCounts.Clear();
        
        // Clear damage events
        lock (_damageEventsLock)
        {
            _damageEvents.Clear();
        }
        
        // Reset game over tracking
        _allEnemiesDefeated = false;
        _lastEnemyDeathTime = DateTime.MinValue;
        _victoryPauseTime = DateTime.MinValue;
        
        // Spawn fresh enemies and factories
        var factoryCount = _random.Next(1, 3); // 1-2 factories
        _logger.LogInformation("Spawning {Count} factories for new game", factoryCount);
        SpawnFactories(factoryCount);
        
        SpawnAsteroids();
        
        SpawnEnemies(2, EnemySubType.Kamikaze);
        SpawnEnemies(2, EnemySubType.Sniper);
        SpawnEnemies(1, EnemySubType.Strafing);
        SpawnEnemies(1, EnemySubType.Scout);
        
        // Notify players that game has restarted
        await NotifyAllPlayersGameRestarted();
        
        // Resume playing
        _currentPhase = GamePhase.Playing;
        _logger.LogInformation("Game restarted!");
        
        // Notify RPC clients through GameEventBroker
        _gameEventBroker.RaiseGameRestart();
        _logger.LogInformation("Game restart event raised through GameEventBroker");
        
        // Send restart chat message through GameEventBroker
        var restartMessage = new ChatMessage(
            "System",
            "Game System",
            "🎮 Game has been restarted! Good luck!",
            DateTime.UtcNow,
            true
        );
        _gameEventBroker.RaiseChatMessage(restartMessage);
    }
    
    private async Task NotifyAllPlayersGameRestarted()
    {
        try
        {
            var tasks = new List<Task>();
            foreach (var playerId in _entities.Where(e => e.Value.Type == EntityType.Player).Select(e => e.Key))
            {
                var playerGrain = _orleansClient.GetGrain<IPlayerGrain>(playerId);
                tasks.Add(playerGrain.NotifyGameRestarted());
            }
            
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to notify players of game restart");
        }
    }
    
    private void UpdateFps(DateTime now)
    {
        lock (_fpsLock)
        {
            // Add current timestamp
            _frameTimestamps.Enqueue(now);
            
            // Remove timestamps older than 10 seconds
            var cutoff = now.AddSeconds(-10);
            while (_frameTimestamps.Count > 0 && _frameTimestamps.Peek() < cutoff)
            {
                _frameTimestamps.Dequeue();
            }
            
            // Calculate FPS based on frame count in the last 10 seconds
            if (_frameTimestamps.Count >= 2)
            {
                var oldestTimestamp = _frameTimestamps.Peek();
                var timeSpan = (now - oldestTimestamp).TotalSeconds;
                _currentFps = _frameTimestamps.Count / timeSpan;
            }
            else
            {
                _currentFps = 0;
            }
        }
    }
    
    public double GetServerFps()
    {
        lock (_fpsLock)
        {
            return _currentFps;
        }
    }
    
    public void RemoveBullet(string bulletId)
    {
        if (_entities.TryRemove(bulletId, out var bullet))
        {
            _logger.LogDebug("[BULLET_DESTROY] Immediately removed bullet {BulletId} from zone ({ZoneX},{ZoneY})", 
                bulletId, _assignedSquare.X, _assignedSquare.Y);
        }
    }
    
    private async Task NotifyNeighborZonesBulletDestroyed(string bulletId)
    {
        try
        {
            // Get all neighboring zones (8-way)
            var neighborZones = new List<GridSquare>();
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue; // Skip our own zone
                    neighborZones.Add(new GridSquare(_assignedSquare.X + dx, _assignedSquare.Y + dy));
                }
            }
            
            // Notify each neighbor zone about the bullet destruction
            var tasks = neighborZones.Select(async zone =>
            {
                try
                {
                    await _crossZoneRpc.NotifyBulletDestroyed(zone, bulletId);
                    _logger.LogDebug("[BULLET_DESTROY] Notified zone ({ZoneX},{ZoneY}) about bullet {BulletId} destruction", 
                        zone.X, zone.Y, bulletId);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[BULLET_DESTROY] Failed to notify zone ({ZoneX},{ZoneY}) about bullet {BulletId}", 
                        zone.X, zone.Y, bulletId);
                }
            });
            
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BULLET_DESTROY] Failed to notify neighbor zones about bullet {BulletId}", bulletId);
        }
    }
    
    private List<PlayerScore> GeneratePlayerScores()
    {
        var playerScores = new List<PlayerScore>();
        var random = new Random();
        
        foreach (var (playerId, entity) in _entities.Where(e => e.Value.Type == EntityType.Player))
        {
            var respawnCount = _playerRespawnCounts.GetValueOrDefault(playerId, 0);
            
            // Generate mock scores
            var enemiesKilled = random.Next(5, 15);
            var playerKills = random.Next(0, 3);
            var deaths = respawnCount;
            var accuracyPercent = (float)(random.NextDouble() * 20 + 80); // 80-100%
            
            // Calculate total score: (Enemies × 10) + (PK × 50) - (Deaths × 5) + (Accuracy bonus)
            var accuracyBonus = (int)((accuracyPercent - 80) * 2); // 0-40 bonus points
            var totalScore = (enemiesKilled * 10) + (playerKills * 50) - (deaths * 5) + accuracyBonus;
            
            playerScores.Add(new PlayerScore(
                playerId, 
                entity.PlayerName ?? "Unknown", 
                respawnCount,
                enemiesKilled,
                playerKills,
                deaths,
                accuracyPercent,
                totalScore));
        }
        
        return playerScores.OrderByDescending(s => s.TotalScore).ToList();
    }
    
    private void SendVictoryScoresMessage(List<PlayerScore> playerScores)
    {
        var scoreText = "🎉 Victory! Final Scores:\n" + string.Join("\n", 
            playerScores.Take(5).Select((s, i) => 
                $"• {s.PlayerName}: {s.TotalScore} pts ({s.EnemiesKilled} enemies, {s.PlayerKills} PK, {s.Deaths} deaths, {s.AccuracyPercent:F0}% accuracy)"));
                
        if (playerScores.Count > 5)
        {
            scoreText += $"\n... and {playerScores.Count - 5} others";
        }
        
        scoreText += "\n\nGame will restart in 10 seconds...";
        
        var victoryMessage = new ChatMessage(
            "System",
            "Game System",
            scoreText,
            DateTime.UtcNow,
            true
        );
        
        _gameEventBroker.RaiseChatMessage(victoryMessage);
    }
    
    private async Task HandleVictoryPause()
    {
        var elapsed = (DateTime.UtcNow - _victoryPauseTime).TotalSeconds;
        var remaining = 10 - (int)elapsed;
        
        // Send countdown messages every 2 seconds (at 8s, 6s, 4s, 2s remaining)
        if (remaining > 0 && remaining % 2 == 0 && remaining <= 8)
        {
            var countdownMessage = new ChatMessage(
                "System",
                "Game System",
                $"Game restarting in {remaining} seconds...",
                DateTime.UtcNow,
                true
            );
            
            _gameEventBroker.RaiseChatMessage(countdownMessage);
        }
        
        // Check if pause is over - transition to GameOver phase
        if (elapsed >= 10)
        {
            _currentPhase = GamePhase.GameOver;
            _gameOverTime = DateTime.UtcNow;
            _logger.LogInformation("Victory pause ended, entering game over phase");

            // Generate scores again for GameOver message
            var playerScores = GeneratePlayerScores();
            var gameOverMessage = new GameOverMessage(playerScores, _gameOverTime, 15);
            await NotifyAllPlayersGameOver(gameOverMessage);
        }
    }
}

/// <summary>
/// Information about a bullet trajectory that has been received but the bullet
/// hasn't entered our zone yet.
/// </summary>
public class PendingBulletInfo
{
    public required string BulletId { get; init; }
    public required int SubType { get; init; }
    public required Vector2 Origin { get; init; }
    public required Vector2 Velocity { get; init; }
    public required float SpawnTime { get; init; }
    public required float Lifespan { get; init; }
    public string? OwnerId { get; init; }
    public int Team { get; init; }
}
