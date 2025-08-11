using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Shooter.Shared.Models;

namespace Shooter.Client.Common
{
    /// <summary>
    /// Prevents rapid zone transitions that can cause client freezes.
    /// </summary>
    public class ZoneTransitionDebouncer
    {
        private readonly ILogger<ZoneTransitionDebouncer> _logger;
        private readonly object _debounceLock = new object();
        private GridSquare? _lastZone;
        private GridSquare? _pendingZone;
        private DateTime _lastTransitionTime = DateTime.MinValue;
        private DateTime _lastTransitionAttempt = DateTime.MinValue;
        private int _rapidTransitionCount = 0;
        private CancellationTokenSource? _debounceCts;
        
        // Configuration - Reduced delays for faster transitions
        private const int MIN_TIME_BETWEEN_TRANSITIONS_MS = 200; // Minimum 200ms between transitions (reduced from 500ms)
        private const int DEBOUNCE_DELAY_MS = 150; // Wait 150ms to confirm zone change (reduced from 300ms)
        private const int MAX_RAPID_TRANSITIONS = 8; // Max transitions before forcing cooldown (increased tolerance)
        private const int COOLDOWN_PERIOD_MS = 1000; // 1 second cooldown after rapid transitions (reduced from 2000ms)
        private const float ZONE_HYSTERESIS_DISTANCE = 20f; // Must move 20 units into new zone

        public bool IsInCooldown { get; private set; }
        public int RapidTransitionCount => _rapidTransitionCount;

        public ZoneTransitionDebouncer(ILogger<ZoneTransitionDebouncer> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Checks if a zone transition should be allowed, with debouncing and cooldown.
        /// </summary>
        public async Task<bool> ShouldTransitionAsync(
            GridSquare newZone,
            Vector2 playerPosition,
            Func<Task> transitionAction)
        {
            lock (_debounceLock)
            {
                // Check if we're in cooldown
                if (IsInCooldown)
                {
                    var cooldownRemaining = COOLDOWN_PERIOD_MS - (DateTime.UtcNow - _lastTransitionTime).TotalMilliseconds;
                    if (cooldownRemaining > 0)
                    {
                        _logger.LogWarning("[ZONE_DEBOUNCE] In cooldown for {Remaining}ms after {Count} rapid transitions",
                            cooldownRemaining, _rapidTransitionCount);
                        return false;
                    }
                    else
                    {
                        IsInCooldown = false;
                        _rapidTransitionCount = 0;
                        _logger.LogInformation("[ZONE_DEBOUNCE] Cooldown ended, transitions enabled");
                    }
                }

                // Check if this is the same zone
                if (_lastZone != null && newZone.X == _lastZone.X && newZone.Y == _lastZone.Y)
                {
                    return false;
                }

                // Check minimum time between transitions
                var timeSinceLastTransition = (DateTime.UtcNow - _lastTransitionTime).TotalMilliseconds;
                if (timeSinceLastTransition < MIN_TIME_BETWEEN_TRANSITIONS_MS)
                {
                    _logger.LogDebug("[ZONE_DEBOUNCE] Rejecting transition, only {Time}ms since last transition",
                        timeSinceLastTransition);
                    return false;
                }

                // Check if player has moved far enough into the new zone (hysteresis)
                if (!IsWellInsideZone(playerPosition, newZone))
                {
                    _logger.LogDebug("[ZONE_DEBOUNCE] Player not far enough into zone {Zone} at position {Position}",
                        newZone, playerPosition);
                    return false;
                }

                // Track rapid transitions
                var timeSinceLastAttempt = (DateTime.UtcNow - _lastTransitionAttempt).TotalMilliseconds;
                if (timeSinceLastAttempt < 2000) // Within 2 seconds
                {
                    _rapidTransitionCount++;
                    if (_rapidTransitionCount >= MAX_RAPID_TRANSITIONS)
                    {
                        IsInCooldown = true;
                        _lastTransitionTime = DateTime.UtcNow;
                        _logger.LogWarning("[ZONE_DEBOUNCE] Too many rapid transitions ({Count}), entering cooldown",
                            _rapidTransitionCount);
                        return false;
                    }
                }
                else
                {
                    _rapidTransitionCount = 0; // Reset counter if transitions have slowed
                }

                _lastTransitionAttempt = DateTime.UtcNow;
                _pendingZone = newZone;

                // Cancel previous debounce
                _debounceCts?.Cancel();
                _debounceCts = new CancellationTokenSource();
            }

            // Debounce the transition
            try
            {
                await Task.Delay(DEBOUNCE_DELAY_MS, _debounceCts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("[ZONE_DEBOUNCE] Transition to zone {Zone} cancelled by newer transition", newZone);
                return false;
            }

            // Confirm the transition
            lock (_debounceLock)
            {
                if (_pendingZone == null || _pendingZone.X != newZone.X || _pendingZone.Y != newZone.Y)
                {
                    _logger.LogDebug("[ZONE_DEBOUNCE] Transition to zone {Zone} cancelled, pending zone changed", newZone);
                    return false;
                }

                _logger.LogInformation("[ZONE_DEBOUNCE] Allowing transition to zone {Zone} after debounce", newZone);
                _lastZone = newZone;
                _lastTransitionTime = DateTime.UtcNow;
                _pendingZone = null;
            }

            // Execute the transition
            try
            {
                await transitionAction();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ZONE_DEBOUNCE] Transition to zone {Zone} failed", newZone);
                
                // On failure, allow retry sooner
                lock (_debounceLock)
                {
                    _lastTransitionTime = DateTime.UtcNow.AddMilliseconds(-MIN_TIME_BETWEEN_TRANSITIONS_MS / 2);
                }
                
                return false;
            }
        }

        /// <summary>
        /// Checks if the player is well inside the zone (not near borders).
        /// </summary>
        private bool IsWellInsideZone(Vector2 position, GridSquare zone)
        {
            var (min, max) = zone.GetBounds();
            
            // Calculate distance from each border
            var distFromLeft = position.X - min.X;
            var distFromRight = max.X - position.X;
            var distFromBottom = position.Y - min.Y;
            var distFromTop = max.Y - position.Y;
            
            // Player must be at least ZONE_HYSTERESIS_DISTANCE units from all borders
            return distFromLeft >= ZONE_HYSTERESIS_DISTANCE &&
                   distFromRight >= ZONE_HYSTERESIS_DISTANCE &&
                   distFromBottom >= ZONE_HYSTERESIS_DISTANCE &&
                   distFromTop >= ZONE_HYSTERESIS_DISTANCE;
        }

        /// <summary>
        /// Resets the debouncer state.
        /// </summary>
        public void Reset()
        {
            lock (_debounceLock)
            {
                _lastZone = null;
                _pendingZone = null;
                _rapidTransitionCount = 0;
                IsInCooldown = false;
                _debounceCts?.Cancel();
                _debounceCts = null;
                _logger.LogInformation("[ZONE_DEBOUNCE] Debouncer reset");
            }
        }

        /// <summary>
        /// Forces the debouncer out of cooldown.
        /// </summary>
        public void ForceEndCooldown()
        {
            lock (_debounceLock)
            {
                if (IsInCooldown)
                {
                    IsInCooldown = false;
                    _rapidTransitionCount = 0;
                    _logger.LogInformation("[ZONE_DEBOUNCE] Cooldown force ended");
                }
            }
        }

        /// <summary>
        /// Gets diagnostic information about the debouncer state.
        /// </summary>
        public string GetDiagnostics()
        {
            lock (_debounceLock)
            {
                var timeSinceLastTransition = (DateTime.UtcNow - _lastTransitionTime).TotalSeconds;
                return $"LastZone: {_lastZone}, InCooldown: {IsInCooldown}, " +
                       $"RapidCount: {_rapidTransitionCount}, LastTransition: {timeSinceLastTransition:F1}s ago";
            }
        }
    }
}