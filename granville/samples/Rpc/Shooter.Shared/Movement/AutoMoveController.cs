using Microsoft.Extensions.Logging;
using Shooter.Shared.Models;

namespace Shooter.Shared.Movement;

/// <summary>
/// Shared automove controller that can be used by both bots and players.
/// Implements various movement modes and strategies.
/// </summary>
public class AutoMoveController
{
    private readonly ILogger _logger;
    private readonly string _entityId;
    private readonly bool _isTestMode;
    
    // Movement modes
    public enum MoveMode
    {
        IntraZone = 1,      // Move within zone for a second, then reevaluate
        RandomInterZone = 2, // Move to random neighboring zone
        PredictableInterZone = 3, // Move to predictable neighboring zone (full coverage)
        StrafeEnemy = 4     // Strafe nearby enemy
    }
    
    // State tracking
    private MoveMode _currentMode = MoveMode.IntraZone;
    private DateTime _zoneEntryTime = DateTime.UtcNow;
    private GridSquare? _currentZone = null;
    private Vector2 _currentMoveDirection = Vector2.Zero;
    private DateTime _lastDirectionChange = DateTime.UtcNow;
    private int _predictableZoneIndex = 0;
    private readonly Random _random = new();
    
    // Inter-zone movement tracking
    private GridSquare? _randomTargetZone = null;
    
    // Strafe direction tracking
    private bool _strafeClockwise = true; // true = clockwise, false = counter-clockwise
    private DateTime _lastStrafeDirectionChange = DateTime.UtcNow;
    
    // Configuration
    private const float BorderDistance = 100f;
    private const float IntraZoneMoveTime = 1f; // seconds
    private const float ZoneStayTime = 15f; // seconds
    private const float EnemyDetectionRange = 300f;
    private const float EnemyStrafeDistance = 150f;
    private const float EnemyBackAwayDistance = 100f;
    private const float StrafeDirectionChangeCooldown = 1.0f; // seconds to prevent rapid direction changes
    
    public AutoMoveController(ILogger logger, string entityId, bool isTestMode)
    {
        _logger = logger;
        _entityId = entityId;
        _isTestMode = isTestMode;
    }
    
    /// <summary>
    /// Updates the automove controller and returns the desired movement and shoot directions.
    /// </summary>
    public (Vector2? moveDirection, Vector2? shootDirection) Update(
        WorldState worldState,
        List<GridSquare> availableZones,
        Vector2 currentPosition)
    {
        // Update current zone tracking
        var newZone = GridSquare.FromPosition(currentPosition);
        if (_currentZone == null || _currentZone.X != newZone.X || _currentZone.Y != newZone.Y)
        {
            _currentZone = newZone;
            _zoneEntryTime = DateTime.UtcNow;
            _logger.LogInformation("AutoMove {EntityId}: Entered zone ({X},{Y})", _entityId, newZone.X, newZone.Y);
        }
        
        // Find nearby enemies, factories, and asteroids
        var nearbyEnemies = worldState.Entities
            .Where(e => e.Type == EntityType.Enemy && e.Health > 0)
            .Where(e => currentPosition.DistanceTo(e.Position) <= EnemyDetectionRange)
            .OrderBy(e => currentPosition.DistanceTo(e.Position))
            .ToList();
            
        var nearbyFactories = worldState.Entities
            .Where(e => e.Type == EntityType.Factory && e.Health > 0)
            .Where(e => currentPosition.DistanceTo(e.Position) <= EnemyDetectionRange)
            .OrderBy(e => currentPosition.DistanceTo(e.Position))
            .ToList();
            
        var nearbyAsteroids = worldState.Entities
            .Where(e => e.Type == EntityType.Asteroid && e.Health > 0)
            .Where(e => currentPosition.DistanceTo(e.Position) <= EnemyDetectionRange)
            .OrderBy(e => currentPosition.DistanceTo(e.Position))
            .ToList();
        
        var hasEnemies = nearbyEnemies.Any();
        var hasTargets = hasEnemies || nearbyFactories.Any() || nearbyAsteroids.Any();
        var timeInZone = (DateTime.UtcNow - _zoneEntryTime).TotalSeconds;
        
        // Determine mode based on conditions
        if (hasTargets && _currentMode != MoveMode.StrafeEnemy)
        {
            _currentMode = MoveMode.StrafeEnemy;
            // Reset strafe direction when entering combat mode
            _strafeClockwise = _random.NextSingle() < 0.5f; // Random initial direction
            _lastStrafeDirectionChange = DateTime.UtcNow;
            // Clear random target zone when switching away from RandomInterZone
            _randomTargetZone = null;
            _logger.LogDebug("AutoMove {EntityId}: Switching to StrafeEnemy mode, initial strafe direction: {Direction}", 
                _entityId, _strafeClockwise ? "clockwise" : "counter-clockwise");
        }
        else if (!hasTargets && _currentMode == MoveMode.StrafeEnemy)
        {
            // No more enemies
            if (!_isTestMode)
            {
                // Not test mode: immediately go to next zone
                _currentMode = MoveMode.RandomInterZone;
                // Clear random target zone when entering RandomInterZone mode
                _randomTargetZone = null;
                _logger.LogDebug("AutoMove {EntityId}: No enemies, switching to RandomInterZone", _entityId);
            }
            else
            {
                // Test mode: stay in IntraZone until 15 seconds
                _currentMode = MoveMode.IntraZone;
                // Clear random target zone when switching away from RandomInterZone
                _randomTargetZone = null;
                _logger.LogDebug("AutoMove {EntityId}: No enemies, switching to IntraZone (test mode)", _entityId);
            }
        }
        else if (timeInZone >= ZoneStayTime && (_currentMode == MoveMode.IntraZone || _currentMode == MoveMode.StrafeEnemy))
        {
            // Time to change zones
            if (_isTestMode)
            {
                _currentMode = MoveMode.PredictableInterZone;
                // Clear random target zone when switching away from RandomInterZone
                _randomTargetZone = null;
                _logger.LogDebug("AutoMove {EntityId}: Time up, switching to PredictableInterZone (test mode)", _entityId);
            }
            else
            {
                _currentMode = MoveMode.RandomInterZone;
                // Clear random target zone when entering RandomInterZone mode
                _randomTargetZone = null;
                _logger.LogDebug("AutoMove {EntityId}: Time up, switching to RandomInterZone", _entityId);
            }
        }
        
        // Execute current mode
        return _currentMode switch
        {
            MoveMode.IntraZone => ExecuteIntraZoneMode(currentPosition, availableZones),
            MoveMode.RandomInterZone => ExecuteRandomInterZoneMode(currentPosition, availableZones),
            MoveMode.PredictableInterZone => ExecutePredictableInterZoneMode(currentPosition, availableZones),
            MoveMode.StrafeEnemy => ExecuteStrafeEnemyMode(currentPosition, GetPrioritizedTarget(nearbyEnemies, nearbyFactories, nearbyAsteroids, currentPosition)),
            _ => (null, null)
        };
    }
    
    private EntityState? GetPrioritizedTarget(List<EntityState> enemies, List<EntityState> factories, List<EntityState> asteroids, Vector2 currentPosition)
    {
        // Priority 1: Kamikaze enemies (they seek to collide)
        var kamikazeEnemies = enemies.Where(e => e.SubType == (int)EnemySubType.Kamikaze).ToList();
        if (kamikazeEnemies.Any())
        {
            return kamikazeEnemies.OrderBy(e => currentPosition.DistanceTo(e.Position)).First();
        }
        
        // Priority 2: All other enemies by distance
        if (enemies.Any())
        {
            return enemies.OrderBy(e => currentPosition.DistanceTo(e.Position)).First();
        }
        
        // Priority 3: Factories
        if (factories.Any())
        {
            return factories.OrderBy(e => currentPosition.DistanceTo(e.Position)).First();
        }
        
        // Priority 4: Asteroids (lowest priority)
        if (asteroids.Any())
        {
            return asteroids.OrderBy(e => currentPosition.DistanceTo(e.Position)).First();
        }
        
        return null;
    }
    
    private (Vector2? moveDirection, Vector2? shootDirection) ExecuteIntraZoneMode(
        Vector2 currentPosition, List<GridSquare> availableZones)
    {
        var timeSinceDirectionChange = (DateTime.UtcNow - _lastDirectionChange).TotalSeconds;
        
        // Change direction every second or if we need to avoid border
        if (timeSinceDirectionChange >= IntraZoneMoveTime || _currentMoveDirection == Vector2.Zero)
        {
            var (min, max) = _currentZone!.GetBounds();
            
            // Check if near any border
            var nearLeftBorder = currentPosition.X - min.X < BorderDistance;
            var nearRightBorder = max.X - currentPosition.X < BorderDistance;
            var nearBottomBorder = currentPosition.Y - min.Y < BorderDistance;
            var nearTopBorder = max.Y - currentPosition.Y < BorderDistance;
            
            if (nearLeftBorder || nearRightBorder || nearBottomBorder || nearTopBorder)
            {
                // Move toward distant wall
                var targetX = nearLeftBorder ? max.X - BorderDistance : (nearRightBorder ? min.X + BorderDistance : currentPosition.X);
                var targetY = nearBottomBorder ? max.Y - BorderDistance : (nearTopBorder ? min.Y + BorderDistance : currentPosition.Y);
                
                var target = new Vector2(targetX, targetY);
                _currentMoveDirection = (target - currentPosition).Normalized() * 100f;
                _logger.LogDebug("AutoMove {EntityId}: Near border, moving toward distant wall", _entityId);
            }
            else
            {
                // Pick random direction
                var angle = _random.NextSingle() * MathF.PI * 2;
                _currentMoveDirection = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 100f;
                _logger.LogDebug("AutoMove {EntityId}: Random direction in zone", _entityId);
            }
            
            _lastDirectionChange = DateTime.UtcNow;
        }
        
        return (_currentMoveDirection, null);
    }
    
    private (Vector2? moveDirection, Vector2? shootDirection) ExecuteRandomInterZoneMode(
        Vector2 currentPosition, List<GridSquare> availableZones)
    {
        if (!availableZones.Any() || _currentZone == null) return (null, null);
        
        // If we don't have a target zone, or we've reached it, pick a new one
        if (_randomTargetZone == null || HasReachedTargetZone(currentPosition, _randomTargetZone))
        {
            // Find neighboring available zones
            var neighbors = GetNeighboringZones(_currentZone, availableZones);
            
            if (!neighbors.Any())
            {
                _logger.LogWarning("AutoMove {EntityId}: No neighboring zones available", _entityId);
                return ExecuteIntraZoneMode(currentPosition, availableZones);
            }
            
            // Pick random neighbor as new target
            _randomTargetZone = neighbors[_random.Next(neighbors.Count)];
            _logger.LogDebug("AutoMove {EntityId}: Selected new random target zone ({X},{Y})", 
                _entityId, _randomTargetZone.X, _randomTargetZone.Y);
        }
        
        // Move toward the persistent target zone
        var targetPos = _randomTargetZone.GetCenter();
        var direction = (targetPos - currentPosition).Normalized() * 100f;
        
        _logger.LogDebug("AutoMove {EntityId}: Moving to target zone ({X},{Y})", 
            _entityId, _randomTargetZone.X, _randomTargetZone.Y);
        
        return (direction, null);
    }
    
    private (Vector2? moveDirection, Vector2? shootDirection) ExecutePredictableInterZoneMode(
        Vector2 currentPosition, List<GridSquare> availableZones)
    {
        if (!availableZones.Any() || _currentZone == null) return (null, null);
        
        // Sort zones by X then Y for predictable ordering
        var sortedZones = availableZones.OrderBy(z => z.Y).ThenBy(z => z.X).ToList();
        
        // Find current zone index
        var currentIndex = sortedZones.FindIndex(z => z.X == _currentZone.X && z.Y == _currentZone.Y);
        if (currentIndex < 0)
        {
            _logger.LogWarning("AutoMove {EntityId}: Current zone not in available zones", _entityId);
            return ExecuteIntraZoneMode(currentPosition, availableZones);
        }
        
        // Move to next zone in sequence
        var nextIndex = (_predictableZoneIndex + 1) % sortedZones.Count;
        var targetZone = sortedZones[nextIndex];
        
        // Check if we're close enough to target zone center
        var targetPos = targetZone.GetCenter();
        var distance = currentPosition.DistanceTo(targetPos);
        
        if (distance < 100f)
        {
            // Reached target, update index
            _predictableZoneIndex = nextIndex;
            _logger.LogInformation("AutoMove {EntityId}: Reached predictable zone ({X},{Y}), index {Index}", 
                _entityId, targetZone.X, targetZone.Y, nextIndex);
            
            // Move to next zone
            nextIndex = (_predictableZoneIndex + 1) % sortedZones.Count;
            targetZone = sortedZones[nextIndex];
            targetPos = targetZone.GetCenter();
        }
        
        var direction = (targetPos - currentPosition).Normalized() * 100f;
        
        _logger.LogDebug("AutoMove {EntityId}: Moving to predictable zone ({X},{Y}), distance {Distance:F1}", 
            _entityId, targetZone.X, targetZone.Y, distance);
        
        return (direction, null);
    }
    
    private (Vector2? moveDirection, Vector2? shootDirection) ExecuteStrafeEnemyMode(
        Vector2 currentPosition, EntityState? nearestTarget)
    {
        if (nearestTarget == null) return (null, null);
        
        var distance = currentPosition.DistanceTo(nearestTarget.Position);
        var direction = (nearestTarget.Position - currentPosition).Normalized();
        
        Vector2? moveDir = null;
        Vector2? shootDir = direction; // Always shoot at target
        
        // Get zone boundaries for staying within zone
        var (min, max) = _currentZone!.GetBounds();
        
        if (distance > EnemyStrafeDistance * 1.5f)
        {
            // Move closer
            moveDir = direction * 100f;
            _logger.LogDebug("AutoMove {EntityId}: Moving closer to target", _entityId);
        }
        else if (distance < EnemyBackAwayDistance)
        {
            // Too close, back away
            var backAwayDir = new Vector2(-direction.X, -direction.Y);
            
            // Check if backing away would take us out of zone
            var futurePos = currentPosition + backAwayDir * 5f; // Check 5 units ahead
            if (futurePos.X < min.X + BorderDistance || futurePos.X > max.X - BorderDistance ||
                futurePos.Y < min.Y + BorderDistance || futurePos.Y > max.Y - BorderDistance)
            {
                // Would go out of zone, strafe instead
                var strafeDir = new Vector2(-direction.Y, direction.X);
                moveDir = strafeDir * 100f;
                _logger.LogDebug("AutoMove {EntityId}: Would leave zone backing away, strafing instead", _entityId);
            }
            else
            {
                moveDir = backAwayDir * 100f;
                _logger.LogDebug("AutoMove {EntityId}: Backing away from target", _entityId);
            }
        }
        else
        {
            // Good distance, strafe
            var strafeDir = new Vector2(-direction.Y, direction.X);
            
            // Check if we're near a zone boundary
            var nearLeftBorder = currentPosition.X - min.X < BorderDistance;
            var nearRightBorder = max.X - currentPosition.X < BorderDistance;
            var nearBottomBorder = currentPosition.Y - min.Y < BorderDistance;
            var nearTopBorder = max.Y - currentPosition.Y < BorderDistance;
            
            // If very close to border (within 25 units), move away from it
            var veryNearBorder = false;
            if (currentPosition.X - min.X < 25f || max.X - currentPosition.X < 25f ||
                currentPosition.Y - min.Y < 25f || max.Y - currentPosition.Y < 25f)
            {
                veryNearBorder = true;
                // Move toward zone center instead of strafing
                var centerX = (min.X + max.X) / 2f;
                var centerY = (min.Y + max.Y) / 2f;
                var toCenter = new Vector2(centerX - currentPosition.X, centerY - currentPosition.Y).Normalized();
                moveDir = toCenter * 100f;
                _logger.LogDebug("AutoMove {EntityId}: Very near zone border at {Position}, moving toward center", _entityId, currentPosition);
            }
            else
            {
                // Normal strafing logic with persistent direction
                // Calculate base strafe direction based on current persistent direction
                strafeDir = _strafeClockwise ? 
                    new Vector2(-direction.Y, direction.X) :   // Clockwise
                    new Vector2(direction.Y, -direction.X);    // Counter-clockwise
                
                var now = DateTime.UtcNow;
                var timeSinceLastStrafeChange = (now - _lastStrafeDirectionChange).TotalSeconds;
                
                // Only consider changing direction if cooldown has passed
                if (timeSinceLastStrafeChange >= StrafeDirectionChangeCooldown)
                {
                    bool shouldChangeDirection = false;
                    
                    // Check if current strafe direction would hit a border
                    if (nearLeftBorder && strafeDir.X < 0 || nearRightBorder && strafeDir.X > 0 ||
                        nearBottomBorder && strafeDir.Y < 0 || nearTopBorder && strafeDir.Y > 0)
                    {
                        shouldChangeDirection = true;
                        _logger.LogDebug("AutoMove {EntityId}: Near zone border, changing strafe direction from {OldDir} to {NewDir}", 
                            _entityId, _strafeClockwise ? "clockwise" : "counter-clockwise", 
                            _strafeClockwise ? "counter-clockwise" : "clockwise");
                    }
                    else if (_random.NextSingle() < 0.05f) // Reduced from 10% to 5% chance to change direction
                    {
                        shouldChangeDirection = true;
                        _logger.LogDebug("AutoMove {EntityId}: Random strafe direction change from {OldDir} to {NewDir}", 
                            _entityId, _strafeClockwise ? "clockwise" : "counter-clockwise", 
                            _strafeClockwise ? "counter-clockwise" : "clockwise");
                    }
                    
                    if (shouldChangeDirection)
                    {
                        _strafeClockwise = !_strafeClockwise;
                        _lastStrafeDirectionChange = now;
                        
                        // Recalculate strafe direction with new persistent direction
                        strafeDir = _strafeClockwise ? 
                            new Vector2(-direction.Y, direction.X) :   // Clockwise
                            new Vector2(direction.Y, -direction.X);    // Counter-clockwise
                    }
                }
                
                if (!veryNearBorder)
                {
                    moveDir = strafeDir * 100f;
                }
            }
            
            var targetType = nearestTarget.Type == EntityType.Factory ? "factory" : "enemy";
            _logger.LogDebug("AutoMove {EntityId}: Strafing {TargetType} at distance {Distance:F1}", _entityId, targetType, distance);
        }
        
        return (moveDir, shootDir);
    }
    
    private List<GridSquare> GetNeighboringZones(GridSquare current, List<GridSquare> availableZones)
    {
        var neighbors = new List<GridSquare>();
        
        // Check all 8 directions (including diagonals)
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue; // Skip current zone
                
                var neighbor = new GridSquare(current.X + dx, current.Y + dy);
                if (availableZones.Any(z => z.X == neighbor.X && z.Y == neighbor.Y))
                {
                    neighbors.Add(neighbor);
                }
            }
        }
        
        return neighbors;
    }
    
    /// <summary>
    /// Gets the current movement mode.
    /// </summary>
    public MoveMode CurrentMode => _currentMode;
    
    /// <summary>
    /// Gets whether the controller is in test mode.
    /// </summary>
    public bool IsTestMode => _isTestMode;
    
    /// <summary>
    /// Resets the controller state.
    /// </summary>
    public void Reset()
    {
        _currentMode = MoveMode.IntraZone;
        _zoneEntryTime = DateTime.UtcNow;
        _currentZone = null;
        _currentMoveDirection = Vector2.Zero;
        _lastDirectionChange = DateTime.UtcNow;
        _predictableZoneIndex = 0;
        _randomTargetZone = null;
        _strafeClockwise = true;
        _lastStrafeDirectionChange = DateTime.UtcNow;
    }
    
    private bool HasReachedTargetZone(Vector2 currentPosition, GridSquare targetZone)
    {
        // Check if we're close to the target zone center or already in the target zone
        var targetCenter = targetZone.GetCenter();
        var distanceToCenter = currentPosition.DistanceTo(targetCenter);
        
        // Consider reached if we're within 100 units of center or if we're in the target zone
        var currentZoneFromPosition = GridSquare.FromPosition(currentPosition);
        var inTargetZone = currentZoneFromPosition.X == targetZone.X && currentZoneFromPosition.Y == targetZone.Y;
        
        return inTargetZone || distanceToCenter < 100f;
    }
}