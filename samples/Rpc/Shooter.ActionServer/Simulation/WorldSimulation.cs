using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shooter.Shared.GrainInterfaces;
using Shooter.Shared.Models;

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

    public async Task<bool> AddPlayer(string playerId)
    {
        try
        {
            var playerGrain = _orleansClient.GetGrain<IPlayerGrain>(playerId);
            var playerInfo = await playerGrain.GetInfo();
            
            var entity = new SimulatedEntity
            {
                EntityId = playerId,
                Type = EntityType.Player,
                Position = playerInfo.Position,
                Velocity = playerInfo.Velocity,
                Health = playerInfo.Health,
                Rotation = 0
            };
            
            _entities[playerId] = entity;
            _playerInputs[playerId] = new PlayerInput();
            
            _logger.LogInformation("Player {PlayerId} added to simulation", playerId);
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
        _entities.TryRemove(playerId, out _);
        _playerInputs.TryRemove(playerId, out _);
        _logger.LogInformation("Player {PlayerId} removed from simulation", playerId);
    }

    public void UpdatePlayerInput(string playerId, Vector2 moveDirection, bool isShooting)
    {
        if (_playerInputs.TryGetValue(playerId, out var input))
        {
            input.MoveDirection = moveDirection;
            input.IsShooting = isShooting;
            input.LastUpdated = DateTime.UtcNow;
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
                e.Rotation))
            .ToList();
            
        return new WorldState(entities, DateTime.UtcNow);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Spawn some initial enemies
        SpawnEnemies(5);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var deltaTime = (float)(now - _lastUpdate).TotalSeconds;
            _lastUpdate = now;
            
            // Update simulation
            UpdatePhysics(deltaTime);
            UpdateAI(deltaTime);
            CheckCollisions();
            CleanupDeadEntities();
            
            // Spawn new enemies occasionally
            if (_random.NextDouble() < 0.01) // 1% chance per frame
            {
                SpawnEnemies(1);
            }
            
            await Task.Delay(16, stoppingToken); // ~60 FPS
        }
    }

    private void UpdatePhysics(float deltaTime)
    {
        // Update player positions based on input
        foreach (var (playerId, input) in _playerInputs)
        {
            if (_entities.TryGetValue(playerId, out var player))
            {
                var speed = 200f; // units per second
                player.Velocity = input.MoveDirection.Normalized() * speed;
                player.Position += player.Velocity * deltaTime;
                
                // Keep player in bounds
                var (min, max) = _assignedSquare.GetBounds();
                player.Position = new Vector2(
                    Math.Clamp(player.Position.X, min.X, max.X),
                    Math.Clamp(player.Position.Y, min.Y, max.Y));
                
                // Handle shooting
                if (input.IsShooting && DateTime.UtcNow - input.LastShot > TimeSpan.FromMilliseconds(250))
                {
                    SpawnBullet(player.Position, input.MoveDirection.Normalized());
                    input.LastShot = DateTime.UtcNow;
                }
            }
        }
        
        // Update all entities
        foreach (var entity in _entities.Values)
        {
            if (entity.Type != EntityType.Player)
            {
                entity.Position += entity.Velocity * deltaTime;
            }
        }
    }

    private void UpdateAI(float deltaTime)
    {
        var players = _entities.Values.Where(e => e.Type == EntityType.Player).ToList();
        
        foreach (var enemy in _entities.Values.Where(e => e.Type == EntityType.Enemy))
        {
            if (!players.Any()) continue;
            
            // Find closest player
            var closestPlayer = players.OrderBy(p => p.Position.DistanceTo(enemy.Position)).First();
            
            // Move towards player
            var direction = (closestPlayer.Position - enemy.Position).Normalized();
            enemy.Velocity = direction * 100f; // Enemy speed
            
            // Shoot at player occasionally
            if (_random.NextDouble() < 0.02 && enemy.Position.DistanceTo(closestPlayer.Position) < 300f)
            {
                SpawnBullet(enemy.Position, direction, isEnemyBullet: true);
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
                        e1.Health -= 10f;
                        e2.Health -= 10f;
                    }
                }
            }
        }
    }

    private void CleanupDeadEntities()
    {
        var deadEntities = _entities.Where(kvp => kvp.Value.Health <= 0).ToList();
        foreach (var (id, entity) in deadEntities)
        {
            if (entity.Type != EntityType.Player)
            {
                _entities.TryRemove(id, out _);
            }
        }
    }

    private void SpawnEnemies(int count)
    {
        var (min, max) = _assignedSquare.GetBounds();
        
        for (int i = 0; i < count; i++)
        {
            var position = new Vector2(
                _random.NextSingle() * (max.X - min.X) + min.X,
                _random.NextSingle() * (max.Y - min.Y) + min.Y);
                
            var enemy = new SimulatedEntity
            {
                EntityId = $"enemy_{_nextEntityId++}",
                Type = EntityType.Enemy,
                Position = position,
                Velocity = Vector2.Zero,
                Health = 50f,
                Rotation = 0
            };
            
            _entities[enemy.EntityId] = enemy;
        }
    }

    private void SpawnBullet(Vector2 position, Vector2 direction, bool isEnemyBullet = false)
    {
        var bullet = new SimulatedEntity
        {
            EntityId = $"bullet_{_nextEntityId++}",
            Type = EntityType.Bullet,
            Position = position + direction * 30f, // Spawn slightly ahead
            Velocity = direction * 500f, // Bullet speed
            Health = 1f,
            Rotation = MathF.Atan2(direction.Y, direction.X)
        };
        
        _entities[bullet.EntityId] = bullet;
    }

    private class SimulatedEntity
    {
        public required string EntityId { get; set; }
        public EntityType Type { get; set; }
        public Vector2 Position { get; set; }
        public Vector2 Velocity { get; set; }
        public float Health { get; set; }
        public float Rotation { get; set; }
    }

    private class PlayerInput
    {
        public Vector2 MoveDirection { get; set; }
        public bool IsShooting { get; set; }
        public DateTime LastUpdated { get; set; }
        public DateTime LastShot { get; set; }
    }
}