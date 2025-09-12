using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Logging;
using Shooter.Shared.Models;

namespace Shooter.Client.Common
{
    /// <summary>
    /// Monitors zone transition health and detects anomalies.
    /// </summary>
    public class ZoneTransitionHealthMonitor
    {
        private readonly ILogger<ZoneTransitionHealthMonitor> _logger;
        private readonly object _lock = new object();
        
        // Event for prolonged zone mismatch detection
        public event Action<GridSquare, GridSquare, double>? OnProlongedMismatchDetected;
        
        // Tracking state
        private GridSquare? _currentPlayerZone;
        private GridSquare? _currentServerZone;
        private DateTime _lastZoneMismatchDetected = DateTime.MinValue;
        private DateTime _lastConsecutiveMismatchIncrement = DateTime.MinValue;
        private DateTime _lastWorldStateReceived = DateTime.UtcNow;
        private DateTime _lastPositionUpdate = DateTime.UtcNow;
        private DateTime _transitionStartTime = DateTime.MinValue;
        private Vector2 _lastKnownPosition;
        private int _consecutiveMismatchCount = 0;
        private int _transitionAttempts = 0;
        private int _failedTransitions = 0;
        private int _successfulTransitions = 0;
        private readonly Dictionary<string, DateTime> _preEstablishedConnectionLastUsed = new();
        private readonly Dictionary<string, int> _preEstablishedConnectionFailures = new();
        private readonly Queue<TransitionEvent> _recentTransitions = new();
        private readonly Stopwatch _connectionUptime = new();
        
        // Thresholds for warnings
        private const int MAX_MISMATCH_DURATION_MS = 5000;  // Warn if mismatched for > 5 seconds
        private const int MAX_TRANSITION_DURATION_MS = 10000; // Warn if transition takes > 10 seconds
        private const int STALE_WORLD_STATE_THRESHOLD_MS = 3000; // Warn if no world state for > 3 seconds
        private const int STALE_POSITION_THRESHOLD_MS = 2000; // Warn if position not updated for > 2 seconds
        private const int STALE_CONNECTION_THRESHOLD_MS = 30000; // Warn if pre-established connection unused > 30 seconds
        private const int MAX_CONSECUTIVE_MISMATCHES = 10; // Warn if mismatched repeatedly
        private const int MAX_TRANSITION_ATTEMPTS = 5; // Warn if too many transition attempts
        private const float POSITION_JUMP_THRESHOLD = 100f; // Warn if position jumps > 100 units
        
        public ZoneTransitionHealthMonitor(ILogger<ZoneTransitionHealthMonitor> logger)
        {
            _logger = logger;
            _connectionUptime.Start();
        }
        
        /// <summary>
        /// Updates the player's current zone and position.
        /// </summary>
        public void UpdatePlayerPosition(Vector2 position)
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                
                // Check for position jump
                if (_lastKnownPosition != default)
                {
                    var distance = _lastKnownPosition.DistanceTo(position);
                    if (distance > POSITION_JUMP_THRESHOLD)
                    {
                        _logger.LogWarning("[HEALTH_MONITOR] POSITION_JUMP detected: Player jumped {Distance:F1} units from ({OldX:F1},{OldY:F1}) to ({NewX:F1},{NewY:F1})",
                            distance, _lastKnownPosition.X, _lastKnownPosition.Y, position.X, position.Y);
                    }
                }
                
                _lastKnownPosition = position;
                _lastPositionUpdate = now;
                
                var newZone = GridSquare.FromPosition(position);
                if (_currentPlayerZone == null || newZone.X != _currentPlayerZone.X || newZone.Y != _currentPlayerZone.Y)
                {
                    _logger.LogDebug("[HEALTH_MONITOR] Player moved from zone ({OldX},{OldY}) to ({NewX},{NewY})",
                        _currentPlayerZone?.X ?? -1, _currentPlayerZone?.Y ?? -1, newZone.X, newZone.Y);
                    _currentPlayerZone = newZone;
                }
                
                CheckForAnomalies();
            }
        }
        
        /// <summary>
        /// Updates the server zone the client is connected to.
        /// </summary>
        public void UpdateServerZone(GridSquare? serverZone)
        {
            lock (_lock)
            {
                if (serverZone != null && (_currentServerZone == null || serverZone.X != _currentServerZone.X || serverZone.Y != _currentServerZone.Y))
                {
                    _logger.LogInformation("[HEALTH_MONITOR] Connected to server for zone ({X},{Y})", serverZone.X, serverZone.Y);
                    _currentServerZone = serverZone;
                    _consecutiveMismatchCount = 0; // Reset on successful connection
                    _lastConsecutiveMismatchIncrement = DateTime.MinValue; // Reset timing
                }
            }
        }
        
        /// <summary>
        /// Records that a world state update was received.
        /// </summary>
        public void RecordWorldStateReceived()
        {
            lock (_lock)
            {
                _lastWorldStateReceived = DateTime.UtcNow;
            }
        }
        
        /// <summary>
        /// Records the start of a zone transition.
        /// </summary>
        public void RecordTransitionStart(GridSquare fromZone, GridSquare toZone)
        {
            lock (_lock)
            {
                _transitionStartTime = DateTime.UtcNow;
                _transitionAttempts++;
                
                _logger.LogInformation("[HEALTH_MONITOR] TRANSITION_START: Attempt #{Attempt} from zone ({FromX},{FromY}) to ({ToX},{ToY})",
                    _transitionAttempts, fromZone.X, fromZone.Y, toZone.X, toZone.Y);
                
                if (_transitionAttempts > MAX_TRANSITION_ATTEMPTS)
                {
                    _logger.LogError("[HEALTH_MONITOR] EXCESSIVE_TRANSITIONS: {Count} transition attempts in current session!",
                        _transitionAttempts);
                }
            }
        }
        
        /// <summary>
        /// Records the completion of a zone transition.
        /// </summary>
        public void RecordTransitionComplete(bool success, TimeSpan duration)
        {
            lock (_lock)
            {
                if (success)
                {
                    _successfulTransitions++;
                    _transitionAttempts = 0; // Reset attempts on success
                    _logger.LogInformation("[HEALTH_MONITOR] TRANSITION_SUCCESS: Completed in {Duration}ms",
                        duration.TotalMilliseconds);
                }
                else
                {
                    _failedTransitions++;
                    _logger.LogWarning("[HEALTH_MONITOR] TRANSITION_FAILED: Failed after {Duration}ms. Total failures: {Failures}",
                        duration.TotalMilliseconds, _failedTransitions);
                }
                
                _recentTransitions.Enqueue(new TransitionEvent
                {
                    Timestamp = DateTime.UtcNow,
                    Duration = duration,
                    Success = success
                });
                
                // Keep only last 20 transitions
                while (_recentTransitions.Count > 20)
                {
                    _recentTransitions.Dequeue();
                }
                
                _transitionStartTime = DateTime.MinValue;
                
                // Log statistics
                var recentSuccessRate = CalculateRecentSuccessRate();
                if (recentSuccessRate < 0.5f && _recentTransitions.Count >= 5)
                {
                    _logger.LogError("[HEALTH_MONITOR] LOW_SUCCESS_RATE: Only {Rate:P0} of recent transitions succeeded",
                        recentSuccessRate);
                }
            }
        }
        
        /// <summary>
        /// Updates pre-established connection status.
        /// </summary>
        public void UpdatePreEstablishedConnection(string serverId, bool isConnected, bool wasUsed = false)
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                
                if (wasUsed)
                {
                    _preEstablishedConnectionLastUsed[serverId] = now;
                    _preEstablishedConnectionFailures.Remove(serverId);
                }
                
                if (!isConnected)
                {
                    if (!_preEstablishedConnectionFailures.ContainsKey(serverId))
                    {
                        _preEstablishedConnectionFailures[serverId] = 0;
                    }
                    _preEstablishedConnectionFailures[serverId]++;
                    
                    if (_preEstablishedConnectionFailures[serverId] > 3)
                    {
                        _logger.LogWarning("[HEALTH_MONITOR] PRE_ESTABLISHED_FAILING: Connection to {ServerId} has failed {Count} times",
                            serverId, _preEstablishedConnectionFailures[serverId]);
                    }
                }
            }
        }
        
        /// <summary>
        /// Checks for various anomalies and logs warnings.
        /// </summary>
        private void CheckForAnomalies()
        {
            var now = DateTime.UtcNow;
            
            // Check for zone mismatch duration
            if (_currentPlayerZone != null && _currentServerZone != null)
            {
                bool isMismatched = _currentPlayerZone.X != _currentServerZone.X || _currentPlayerZone.Y != _currentServerZone.Y;
                
                if (isMismatched)
                {
                    if (_lastZoneMismatchDetected == DateTime.MinValue)
                    {
                        _lastZoneMismatchDetected = now;
                        _lastConsecutiveMismatchIncrement = DateTime.MinValue; // Reset increment tracking
                    }
                    
                    var mismatchDuration = (now - _lastZoneMismatchDetected).TotalMilliseconds;
                    
                    // Only increment consecutive count once per second to avoid spam from frequent world state updates
                    var timeSinceLastIncrement = _lastConsecutiveMismatchIncrement == DateTime.MinValue ? 
                        TimeSpan.MaxValue : 
                        (now - _lastConsecutiveMismatchIncrement);
                    
                    if (timeSinceLastIncrement.TotalMilliseconds >= 1000) // 1 second debounce
                    {
                        _consecutiveMismatchCount++;
                        _lastConsecutiveMismatchIncrement = now;
                        
                        // Log chronic mismatch immediately when it happens
                        if (_consecutiveMismatchCount > MAX_CONSECUTIVE_MISMATCHES)
                        {
                            _logger.LogError("[HEALTH_MONITOR] CHRONIC_MISMATCH: Zone mismatch detected {Count} times consecutively!",
                                _consecutiveMismatchCount);
                        }
                    }
                    
                    // Still check for prolonged mismatch on every update
                    if (mismatchDuration > MAX_MISMATCH_DURATION_MS)
                    {
                        _logger.LogError("[HEALTH_MONITOR] PROLONGED_MISMATCH: Player in zone ({PlayerX},{PlayerY}) but connected to server for zone ({ServerX},{ServerY}) for {Duration}ms",
                            _currentPlayerZone.X, _currentPlayerZone.Y,
                            _currentServerZone.X, _currentServerZone.Y,
                            mismatchDuration);
                        
                        // Fire event to trigger forced reconnection
                        OnProlongedMismatchDetected?.Invoke(_currentPlayerZone, _currentServerZone, mismatchDuration);
                    }
                }
                else
                {
                    _lastZoneMismatchDetected = DateTime.MinValue;
                    _lastConsecutiveMismatchIncrement = DateTime.MinValue;
                    _consecutiveMismatchCount = 0;
                }
            }
            
            // Check for stuck transition
            if (_transitionStartTime != DateTime.MinValue)
            {
                var transitionDuration = (now - _transitionStartTime).TotalMilliseconds;
                if (transitionDuration > MAX_TRANSITION_DURATION_MS)
                {
                    _logger.LogError("[HEALTH_MONITOR] STUCK_TRANSITION: Transition has been in progress for {Duration}ms",
                        transitionDuration);
                }
            }
            
            // Check for stale world state
            var worldStateAge = (now - _lastWorldStateReceived).TotalMilliseconds;
            if (worldStateAge > STALE_WORLD_STATE_THRESHOLD_MS)
            {
                _logger.LogWarning("[HEALTH_MONITOR] STALE_WORLD_STATE: No world state received for {Duration}ms",
                    worldStateAge);
            }
            
            // Check for stale position updates
            var positionAge = (now - _lastPositionUpdate).TotalMilliseconds;
            if (positionAge > STALE_POSITION_THRESHOLD_MS)
            {
                _logger.LogWarning("[HEALTH_MONITOR] STALE_POSITION: Position not updated for {Duration}ms",
                    positionAge);
            }
            
            // Check for stale pre-established connections
            foreach (var kvp in _preEstablishedConnectionLastUsed.ToList())
            {
                var age = (now - kvp.Value).TotalMilliseconds;
                if (age > STALE_CONNECTION_THRESHOLD_MS)
                {
                    _logger.LogWarning("[HEALTH_MONITOR] STALE_PRE_ESTABLISHED: Connection to {ServerId} unused for {Duration}ms",
                        kvp.Key, age);
                }
            }
        }
        
        /// <summary>
        /// Calculates the success rate of recent transitions.
        /// </summary>
        private float CalculateRecentSuccessRate()
        {
            if (_recentTransitions.Count == 0) return 1.0f;
            
            int successful = _recentTransitions.Count(t => t.Success);
            return (float)successful / _recentTransitions.Count;
        }
        
        /// <summary>
        /// Gets a health report summary.
        /// </summary>
        public HealthReport GetHealthReport()
        {
            lock (_lock)
            {
                CheckForAnomalies(); // Run checks before generating report
                
                return new HealthReport
                {
                    CurrentPlayerZone = _currentPlayerZone,
                    CurrentServerZone = _currentServerZone,
                    IsZoneMismatched = _currentPlayerZone != null && _currentServerZone != null &&
                                       (_currentPlayerZone.X != _currentServerZone.X || _currentPlayerZone.Y != _currentServerZone.Y),
                    MismatchDuration = _lastZoneMismatchDetected != DateTime.MinValue ? 
                        DateTime.UtcNow - _lastZoneMismatchDetected : TimeSpan.Zero,
                    ConnectionUptime = _connectionUptime.Elapsed,
                    SuccessfulTransitions = _successfulTransitions,
                    FailedTransitions = _failedTransitions,
                    RecentSuccessRate = CalculateRecentSuccessRate(),
                    LastWorldStateAge = DateTime.UtcNow - _lastWorldStateReceived,
                    LastPositionUpdateAge = DateTime.UtcNow - _lastPositionUpdate,
                    IsTransitioning = _transitionStartTime != DateTime.MinValue,
                    TransitionDuration = _transitionStartTime != DateTime.MinValue ?
                        DateTime.UtcNow - _transitionStartTime : TimeSpan.Zero
                };
            }
        }
        
        /// <summary>
        /// Logs a comprehensive health report.
        /// </summary>
        public void LogHealthReport()
        {
            var report = GetHealthReport();
            
            _logger.LogInformation("[HEALTH_MONITOR] === Zone Transition Health Report ===");
            _logger.LogInformation("[HEALTH_MONITOR] Player Zone: ({PlayerX},{PlayerY}), Server Zone: ({ServerX},{ServerY})",
                report.CurrentPlayerZone?.X ?? -1, report.CurrentPlayerZone?.Y ?? -1,
                report.CurrentServerZone?.X ?? -1, report.CurrentServerZone?.Y ?? -1);
            _logger.LogInformation("[HEALTH_MONITOR] Connection Uptime: {Uptime:F1}s, Success Rate: {Rate:P0}",
                report.ConnectionUptime.TotalSeconds, report.RecentSuccessRate);
            _logger.LogInformation("[HEALTH_MONITOR] Transitions: {Success} successful, {Failed} failed",
                report.SuccessfulTransitions, report.FailedTransitions);
            
            if (report.IsZoneMismatched)
            {
                _logger.LogWarning("[HEALTH_MONITOR] ZONE MISMATCH for {Duration:F1}s",
                    report.MismatchDuration.TotalSeconds);
            }
            
            if (report.IsTransitioning)
            {
                _logger.LogWarning("[HEALTH_MONITOR] TRANSITION IN PROGRESS for {Duration:F1}s",
                    report.TransitionDuration.TotalSeconds);
            }
            
            _logger.LogInformation("[HEALTH_MONITOR] =====================================");
        }
        
        private class TransitionEvent
        {
            public DateTime Timestamp { get; set; }
            public TimeSpan Duration { get; set; }
            public bool Success { get; set; }
        }
        
        public class HealthReport
        {
            public GridSquare? CurrentPlayerZone { get; set; }
            public GridSquare? CurrentServerZone { get; set; }
            public bool IsZoneMismatched { get; set; }
            public TimeSpan MismatchDuration { get; set; }
            public TimeSpan ConnectionUptime { get; set; }
            public int SuccessfulTransitions { get; set; }
            public int FailedTransitions { get; set; }
            public float RecentSuccessRate { get; set; }
            public TimeSpan LastWorldStateAge { get; set; }
            public TimeSpan LastPositionUpdateAge { get; set; }
            public bool IsTransitioning { get; set; }
            public TimeSpan TransitionDuration { get; set; }
        }
    }
}