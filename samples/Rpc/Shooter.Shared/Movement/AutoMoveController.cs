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
    
    // Configuration
    private const float BorderDistance = 100f;
    private const float IntraZoneMoveTime = 1f; // seconds
    private const float ZoneStayTime = 15f; // seconds
    private const float EnemyDetectionRange = 300f;
    private const float EnemyStrafeDistance = 150f;
    private const float EnemyBackAwayDistance = 100f;
    
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
        
        // Find nearby enemies
        var nearbyEnemies = worldState.Entities
            .Where(e => e.Type == EntityType.Enemy && e.Health > 0)
            .Where(e => currentPosition.DistanceTo(e.Position) <= EnemyDetectionRange)
            .OrderBy(e => currentPosition.DistanceTo(e.Position))
            .ToList();
        
        var hasEnemies = nearbyEnemies.Any();
        var timeInZone = (DateTime.UtcNow - _zoneEntryTime).TotalSeconds;
        
        // Determine mode based on conditions
        if (hasEnemies && _currentMode != MoveMode.StrafeEnemy)
        {
            _currentMode = MoveMode.StrafeEnemy;
            _logger.LogDebug("AutoMove {EntityId}: Switching to StrafeEnemy mode", _entityId);
        }
        else if (!hasEnemies && _currentMode == MoveMode.StrafeEnemy)
        {
            // No more enemies
            if (!_isTestMode)
            {
                // Not test mode: immediately go to next zone
                _currentMode = MoveMode.RandomInterZone;
                _logger.LogDebug("AutoMove {EntityId}: No enemies, switching to RandomInterZone", _entityId);
            }
            else
            {
                // Test mode: stay in IntraZone until 15 seconds
                _currentMode = MoveMode.IntraZone;
                _logger.LogDebug("AutoMove {EntityId}: No enemies, switching to IntraZone (test mode)", _entityId);
            }
        }
        else if (timeInZone >= ZoneStayTime && (_currentMode == MoveMode.IntraZone || _currentMode == MoveMode.StrafeEnemy))
        {
            // Time to change zones
            if (_isTestMode)
            {
                _currentMode = MoveMode.PredictableInterZone;
                _logger.LogDebug("AutoMove {EntityId}: Time up, switching to PredictableInterZone (test mode)", _entityId);
            }
            else
            {
                _currentMode = MoveMode.RandomInterZone;
                _logger.LogDebug("AutoMove {EntityId}: Time up, switching to RandomInterZone", _entityId);
            }
        }
        
        // Execute current mode
        return _currentMode switch
        {
            MoveMode.IntraZone => ExecuteIntraZoneMode(currentPosition, availableZones),
            MoveMode.RandomInterZone => ExecuteRandomInterZoneMode(currentPosition, availableZones),
            MoveMode.PredictableInterZone => ExecutePredictableInterZoneMode(currentPosition, availableZones),
            MoveMode.StrafeEnemy => ExecuteStrafeEnemyMode(currentPosition, nearbyEnemies.FirstOrDefault()),
            _ => (null, null)
        };
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
        
        // Find neighboring available zones
        var neighbors = GetNeighboringZones(_currentZone, availableZones);
        
        if (!neighbors.Any())
        {
            _logger.LogWarning("AutoMove {EntityId}: No neighboring zones available", _entityId);
            return ExecuteIntraZoneMode(currentPosition, availableZones);
        }
        
        // Pick random neighbor
        var targetZone = neighbors[_random.Next(neighbors.Count)];
        var targetPos = targetZone.GetCenter();
        var direction = (targetPos - currentPosition).Normalized() * 100f;
        
        _logger.LogDebug("AutoMove {EntityId}: Moving to random neighbor zone ({X},{Y})", 
            _entityId, targetZone.X, targetZone.Y);
        
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
        Vector2 currentPosition, EntityState? nearestEnemy)
    {
        if (nearestEnemy == null) return (null, null);
        
        var distance = currentPosition.DistanceTo(nearestEnemy.Position);
        var direction = (nearestEnemy.Position - currentPosition).Normalized();
        
        Vector2? moveDir = null;
        Vector2? shootDir = direction; // Always shoot at enemy
        
        if (distance > EnemyStrafeDistance * 1.5f)
        {
            // Move closer
            moveDir = direction * 100f;
            _logger.LogDebug("AutoMove {EntityId}: Moving closer to enemy", _entityId);
        }
        else if (distance < EnemyBackAwayDistance)
        {
            // Too close, back away
            moveDir = new Vector2(-direction.X, -direction.Y) * 100f;
            _logger.LogDebug("AutoMove {EntityId}: Backing away from enemy", _entityId);
        }
        else
        {
            // Good distance, strafe
            var strafeDir = new Vector2(-direction.Y, direction.X);
            
            // Alternate strafe direction occasionally
            if (_random.NextSingle() < 0.1f) // 10% chance to change direction
            {
                strafeDir = new Vector2(-strafeDir.X, -strafeDir.Y);
            }
            
            moveDir = strafeDir * 100f;
            _logger.LogDebug("AutoMove {EntityId}: Strafing enemy at distance {Distance:F1}", _entityId, distance);
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
    }
}