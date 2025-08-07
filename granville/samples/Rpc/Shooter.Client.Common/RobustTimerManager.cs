using System;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Shooter.Client.Common
{
    /// <summary>
    /// Manages timers robustly during zone transitions and connection issues.
    /// </summary>
    public class RobustTimerManager : IDisposable
    {
        private readonly ILogger<RobustTimerManager> _logger;
        private readonly ConcurrentDictionary<string, ManagedTimer> _timers = new();
        private readonly object _transitionLock = new object();
        private bool _inTransition = false;
        private bool _disposed = false;

        private class ManagedTimer
        {
            public Timer? Timer { get; set; }
            public TimerCallback Callback { get; }
            public int Period { get; }
            public bool IsPaused { get; set; }
            public DateTime LastExecution { get; set; }
            public string Name { get; }

            public ManagedTimer(string name, TimerCallback callback, int period)
            {
                Name = name;
                Callback = callback;
                Period = period;
                LastExecution = DateTime.MinValue;
            }
        }

        public RobustTimerManager(ILogger<RobustTimerManager> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Creates or updates a managed timer.
        /// </summary>
        public void CreateTimer(string name, TimerCallback callback, int periodMs)
        {
            if (_disposed) return;

            var managedTimer = new ManagedTimer(name, callback, periodMs);
            
            if (_timers.TryGetValue(name, out var existing))
            {
                existing.Timer?.Dispose();
            }

            managedTimer.Timer = new Timer(
                state => ExecuteTimerCallback(name, state),
                null,
                periodMs,
                periodMs);

            _timers[name] = managedTimer;
            _logger.LogDebug("[TIMER_MANAGER] Created timer '{Name}' with period {Period}ms", name, periodMs);
        }

        private void ExecuteTimerCallback(string name, object? state)
        {
            if (_disposed || _inTransition) return;

            if (_timers.TryGetValue(name, out var managedTimer) && !managedTimer.IsPaused)
            {
                try
                {
                    managedTimer.LastExecution = DateTime.UtcNow;
                    managedTimer.Callback(state);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[TIMER_MANAGER] Error in timer '{Name}' callback", name);
                }
            }
        }

        /// <summary>
        /// Begins a transition, pausing all timers.
        /// </summary>
        public TransitionScope BeginTransition(string reason)
        {
            lock (_transitionLock)
            {
                _inTransition = true;
                _logger.LogInformation("[TIMER_MANAGER] Beginning transition: {Reason}", reason);
                
                // Pause all timers
                foreach (var timer in _timers.Values)
                {
                    timer.IsPaused = true;
                    timer.Timer?.Change(Timeout.Infinite, Timeout.Infinite);
                }
            }

            return new TransitionScope(this);
        }

        /// <summary>
        /// Ends a transition, resuming all timers.
        /// </summary>
        internal void EndTransition()
        {
            lock (_transitionLock)
            {
                _inTransition = false;
                _logger.LogInformation("[TIMER_MANAGER] Ending transition, resuming timers");
                
                // Resume all timers
                foreach (var timer in _timers.Values)
                {
                    timer.IsPaused = false;
                    timer.Timer?.Change(timer.Period, timer.Period);
                }
            }
        }

        /// <summary>
        /// Stops a specific timer.
        /// </summary>
        public void StopTimer(string name)
        {
            if (_timers.TryRemove(name, out var timer))
            {
                timer.Timer?.Dispose();
                _logger.LogDebug("[TIMER_MANAGER] Stopped timer '{Name}'", name);
            }
        }

        /// <summary>
        /// Restarts all timers (useful after connection recovery).
        /// </summary>
        public void RestartAllTimers()
        {
            lock (_transitionLock)
            {
                _logger.LogInformation("[TIMER_MANAGER] Restarting all timers");
                
                foreach (var timer in _timers.Values)
                {
                    timer.Timer?.Change(0, timer.Period);
                    timer.IsPaused = false;
                }
            }
        }

        /// <summary>
        /// Checks timer health and restarts stuck timers.
        /// </summary>
        public void CheckTimerHealth()
        {
            var now = DateTime.UtcNow;
            
            foreach (var kvp in _timers)
            {
                var timer = kvp.Value;
                if (!timer.IsPaused && timer.LastExecution != DateTime.MinValue)
                {
                    var timeSinceLastExecution = (now - timer.LastExecution).TotalMilliseconds;
                    if (timeSinceLastExecution > timer.Period * 3) // Timer is stuck
                    {
                        _logger.LogWarning("[TIMER_MANAGER] Timer '{Name}' appears stuck, restarting", kvp.Key);
                        timer.Timer?.Change(0, timer.Period);
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var timer in _timers.Values)
            {
                timer.Timer?.Dispose();
            }
            _timers.Clear();
        }

        public class TransitionScope : IDisposable
        {
            private readonly RobustTimerManager _manager;

            internal TransitionScope(RobustTimerManager manager)
            {
                _manager = manager;
            }

            public void Dispose()
            {
                _manager.EndTransition();
            }
        }
    }
}