using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shooter.Shared.GrainInterfaces;
using Shooter.Shared.Models;
using System.Linq;

namespace Shooter.ActionServer.Simulation;

public class WorldSimulation : BackgroundService, IWorldSimulation
{
    private readonly ILogger<WorldSimulation> _logger;
    private readonly Orleans.IClusterClient _orleansClient;
    private readonly ConcurrentDictionary<string, SimulatedEntity> _entities = new();
    private readonly ConcurrentDictionary<string, PlayerInput> _playerInputs = new();
    private readonly Random _random = new();
    private GridSquare _assignedSquare = new GridSquare(0, 0);
    private DateTime _lastUpdate = DateTime.UtcNow;
    private int _nextEntityId = 1;
    private HashSet<GridSquare> _availableZones = new();
    private DateTime _lastZoneCheck = DateTime.MinValue;
    private long _sequenceNumber = 0;

    public WorldSimulation(ILogger<WorldSimulation> logger, Orleans.IClusterClient orleansClient)
    {
        _logger = logger;
        _orleansClient = orleansClient;
    }

    public void SetAssignedSquare(GridSquare square)
    {
        _assignedSquare = square;
        _logger.LogInformation("Assigned to grid square: {X}, {Y}", square.X, square.Y);
    }
    
    public GridSquare GetAssignedSquare()
    {
        return _assignedSquare;
    }

    public async Task<bool> AddPlayer(string playerId)
    {
        try
        {
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
            
            var entity = new SimulatedEntity
            {
                EntityId = playerId,
                Type = EntityType.Player,
                Position = playerInfo.Position,
                Velocity = playerInfo.Velocity,
                Health = playerInfo.Health,
                Rotation = 0,
                State = EntityStateType.Active,
                StateTimer = 0f
            };
            
            _entities[playerId] = entity;
            _playerInputs[playerId] = new PlayerInput();
            
            _logger.LogInformation("Player {PlayerId} added to simulation at position {Position} in zone {Zone}", 
                playerId, entity.Position, _assignedSquare);
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
                e.StateTimer))
            .ToList();
        
        // Log entity counts by type for debugging
        var entityCounts = entities.GroupBy(e => e.Type)
            .ToDictionary(g => g.Key, g => g.Count());
        
        if (entities.Count > 0)
        {
            _logger.LogDebug("GetCurrentState returning {Total} entities: Players={Players}, Enemies={Enemies}, Bullets={Bullets}, Explosions={Explosions}",
                entities.Count,
                entityCounts.GetValueOrDefault(EntityType.Player, 0),
                entityCounts.GetValueOrDefault(EntityType.Enemy, 0),
                entityCounts.GetValueOrDefault(EntityType.Bullet, 0),
                entityCounts.GetValueOrDefault(EntityType.Explosion, 0));
        }
            
        // Increment sequence number for each state update
        var sequenceNumber = Interlocked.Increment(ref _sequenceNumber);
        return new WorldState(entities, DateTime.UtcNow, sequenceNumber);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Spawn some initial enemies of different types
        _logger.LogInformation("Spawning initial enemies in zone {Zone}", _assignedSquare);
        SpawnEnemies(2, EnemySubType.Kamikaze);
        SpawnEnemies(2, EnemySubType.Sniper);
        SpawnEnemies(1, EnemySubType.Strafing);
        _logger.LogInformation("Initial enemy spawn complete. Total entities: {Count}", _entities.Count);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var deltaTime = (float)(now - _lastUpdate).TotalSeconds;
            _lastUpdate = now;
            
            // Update simulation
            await UpdatePhysicsAsync(deltaTime);
            UpdateAI(deltaTime);
            CheckCollisions();
            UpdateEntityStates(deltaTime);
            CleanupDeadEntities();
            
            // Spawn new enemies occasionally
            if (_random.NextDouble() < 0.001) // 0.1% chance per frame (10% of previous rate)
            {
                var enemyType = (EnemySubType)_random.Next(1, 4);
                SpawnEnemies(1, enemyType);
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
                
                var speed = 200f; // units per second
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
                                
                                // First update the position
                                await worldManager.UpdatePlayerPosition(playerId, newPos);
                                _logger.LogInformation("Updated WorldManager position for player {PlayerId} to {Position} in zone {Zone}", 
                                    playerId, newPos, newZone);
                                
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
                        var (min, max) = _assignedSquare.GetBounds();
                        player.Position = new Vector2(
                            Math.Clamp(player.Position.X, min.X, max.X - 0.1f),
                            Math.Clamp(player.Position.Y, min.Y, max.Y - 0.1f)
                        );
                    }
                }
                    
                if (oldPos != player.Position)
                {
                    _logger.LogDebug("Player {PlayerId} moved from {OldPos} to {NewPos}", 
                        playerId, oldPos, player.Position);
                    
                    // Check if player is approaching zone boundary
                    var (min, max) = _assignedSquare.GetBounds();
                    var distToEdge = Math.Min(
                        Math.Min(player.Position.X - min.X, max.X - player.Position.X),
                        Math.Min(player.Position.Y - min.Y, max.Y - player.Position.Y)
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
                    
                    SpawnBullet(player.Position, shootDirection);
                    input.LastShot = DateTime.UtcNow;
                }
            }
        }
        
        // Update all entities
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
            }
        }
    }

    private void UpdateAI(float deltaTime)
    {
        var players = _entities.Values.Where(e => e.Type == EntityType.Player && e.State == EntityStateType.Active).ToList();
        
        foreach (var enemy in _entities.Values.Where(e => e.Type == EntityType.Enemy && e.State == EntityStateType.Active))
        {
            if (!players.Any()) continue;
            
            // Find closest player
            var closestPlayer = players.OrderBy(p => p.Position.DistanceTo(enemy.Position)).First();
            var direction = (closestPlayer.Position - enemy.Position).Normalized();
            var distance = enemy.Position.DistanceTo(closestPlayer.Position);
            
            switch ((EnemySubType)enemy.SubType)
            {
                case EnemySubType.Kamikaze:
                    // Always move towards player at high speed
                    enemy.Velocity = direction * 45f; // 30% of 150f
                    break;
                    
                case EnemySubType.Sniper:
                    // Move to range then stop and shoot
                    if (distance > 250f)
                    {
                        enemy.Velocity = direction * 24f; // 30% of 80f
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
                        enemy.Velocity = direction * 30f; // 30% of 100f
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
                        
                        enemy.Velocity = strafeDirection * enemy.StrafeDirection.Value * 36f; // 30% of 120f
                        
                        // Shoot while strafing
                        if (_random.NextDouble() < 0.03)
                        {
                            SpawnBullet(enemy.Position, direction, isEnemyBullet: true);
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
                
                // Skip dead/dying entities
                if (e1.State != EntityStateType.Active || e2.State != EntityStateType.Active)
                    continue;
                
                var distance = e1.Position.DistanceTo(e2.Position);
                var collisionRadius = 20f; // Simple circular collision
                
                if (distance < collisionRadius)
                {
                    // Handle collision based on entity types
                    if (e1.Type == EntityType.Bullet && e2.Type != EntityType.Bullet)
                    {
                        e2.Health -= 25f;
                        e1.Health = 0; // Destroy bullet
                    }
                    else if (e2.Type == EntityType.Bullet && e1.Type != EntityType.Bullet)
                    {
                        e1.Health -= 25f;
                        e2.Health = 0; // Destroy bullet
                    }
                    else if (e1.Type == EntityType.Player && e2.Type == EntityType.Enemy)
                    {
                        // Kamikaze enemies do more damage
                        var damage = (EnemySubType)e2.SubType == EnemySubType.Kamikaze ? 30f : 10f;
                        e1.Health -= damage;
                        e2.Health -= 10f;
                    }
                    else if (e2.Type == EntityType.Player && e1.Type == EntityType.Enemy)
                    {
                        var damage = (EnemySubType)e1.SubType == EnemySubType.Kamikaze ? 30f : 10f;
                        e2.Health -= damage;
                        e1.Health -= 10f;
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
                            
                            // Respawn at random location within the assigned zone
                            var (min, max) = _assignedSquare.GetBounds();
                            var respawnX = min.X + _random.NextSingle() * (max.X - min.X);
                            var respawnY = min.Y + _random.NextSingle() * (max.Y - min.Y);
                            entity.Position = new Vector2(respawnX, respawnY);
                            entity.Health = 1000f;
                            entity.Velocity = Vector2.Zero;
                            
                            _logger.LogInformation("Player {PlayerId} respawning at position {Position} in zone {Zone}", 
                                entity.EntityId, entity.Position, _assignedSquare);
                                
                            // Update player position in Orleans to ensure consistency
                            _ = Task.Run(async () => {
                                try
                                {
                                    var playerGrain = _orleansClient.GetGrain<IPlayerGrain>(entity.EntityId);
                                    await playerGrain.UpdatePosition(entity.Position, Vector2.Zero);
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
            // Handle explosion lifetime
            else if (entity.Type == EntityType.Explosion && entity.StateTimer >= 0.5f)
            {
                entity.Health = 0; // Mark for cleanup
            }
        }
    }

    private void CleanupDeadEntities()
    {
        var deadEntities = _entities.Where(kvp => 
            kvp.Value.Health <= 0 && 
            kvp.Value.Type != EntityType.Player && 
            kvp.Value.State != EntityStateType.Respawning).ToList();
            
        foreach (var (id, entity) in deadEntities)
        {
            _entities.TryRemove(id, out _);
        }
    }

    private void SpawnEnemies(int count, EnemySubType enemyType)
    {
        var (min, max) = _assignedSquare.GetBounds();
        _logger.LogDebug("Spawning {Count} {Type} enemies in zone {Zone} bounds: ({MinX},{MinY}) to ({MaxX},{MaxY})", 
            count, enemyType, _assignedSquare, min.X, min.Y, max.X, max.Y);
        
        for (int i = 0; i < count; i++)
        {
            var position = new Vector2(
                _random.NextSingle() * (max.X - min.X) + min.X,
                _random.NextSingle() * (max.Y - min.Y) + min.Y);
                
            var enemy = new SimulatedEntity
            {
                EntityId = $"enemy_{_nextEntityId++}",
                Type = EntityType.Enemy,
                SubType = (int)enemyType,
                Position = position,
                Velocity = Vector2.Zero,
                Health = enemyType == EnemySubType.Kamikaze ? 30f : 50f, // Kamikaze has less health
                Rotation = 0,
                State = EntityStateType.Active,
                StateTimer = 0f
            };
            
            _entities[enemy.EntityId] = enemy;
            _logger.LogDebug("Spawned {Type} enemy {Id} at position {Position}", enemyType, enemy.EntityId, position);
        }
    }

    private void SpawnBullet(Vector2 position, Vector2 direction, bool isEnemyBullet = false)
    {
        // Enemy bullets are 40% of normal speed (60% slower)
        var bulletSpeed = isEnemyBullet ? 200f : 500f;
        
        var bullet = new SimulatedEntity
        {
            EntityId = $"bullet_{_nextEntityId++}",
            Type = EntityType.Bullet,
            Position = position + direction * 30f, // Spawn slightly ahead
            Velocity = direction * bulletSpeed,
            Health = 1f,
            Rotation = MathF.Atan2(direction.Y, direction.X),
            State = EntityStateType.Active,
            StateTimer = 0f,
            SubType = isEnemyBullet ? 1 : 0 // Track bullet type
        };
        
        _entities[bullet.EntityId] = bullet;
    }

    private void SpawnExplosion(Vector2 position, bool isSmall = false)
    {
        var explosion = new SimulatedEntity
        {
            EntityId = $"explosion_{_nextEntityId++}",
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
            
            // If it's a player, also add to player inputs
            if (type == EntityType.Player)
            {
                _playerInputs[entityId] = new PlayerInput
                {
                    MoveDirection = Vector2.Zero,
                    ShootDirection = null,
                    IsShooting = false,
                    LastUpdated = DateTime.UtcNow
                };
                _logger.LogInformation("[TRANSFER_IN] Player {EntityId} successfully transferred to zone {Zone} at position {Position}", 
                    entityId, _assignedSquare, position);
                    
                // Update player position in Orleans grain for consistency
                _ = Task.Run(async () => {
                    try
                    {
                        var playerGrain = _orleansClient.GetGrain<IPlayerGrain>(entityId);
                        await playerGrain.UpdatePosition(position, velocity);
                        _logger.LogInformation("[TRANSFER_IN] Updated player grain position for {PlayerId} to {Position}", 
                            entityId, position);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[TRANSFER_IN] Failed to update player grain position for {PlayerId}", entityId);
                    }
                });
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
    
    public PlayerInfo? GetPlayerInfo(string playerId)
    {
        if (_entities.TryGetValue(playerId, out var entity) && entity.Type == EntityType.Player)
        {
            return new PlayerInfo(playerId, playerId, entity.Position, entity.Velocity, entity.Health);
        }
        return null;
    }
}
