using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Shooter.Client.Common
{
    /// <summary>
    /// Manages connection resilience with exponential backoff and automatic recovery.
    /// </summary>
    public class ConnectionResilienceManager
    {
        private readonly ILogger<ConnectionResilienceManager> _logger;
        private readonly object _connectionLock = new object();
        private int _reconnectAttempts = 0;
        private DateTime _lastConnectionAttempt = DateTime.MinValue;
        private DateTime _lastSuccessfulConnection = DateTime.MinValue;
        private bool _isReconnecting = false;
        
        // Configuration
        private const int MAX_RECONNECT_ATTEMPTS = 10;
        private const int INITIAL_BACKOFF_MS = 1000;
        private const int MAX_BACKOFF_MS = 30000;
        private const double BACKOFF_MULTIPLIER = 1.5;
        
        public bool IsHealthy => (DateTime.UtcNow - _lastSuccessfulConnection).TotalSeconds < 30;
        public int ReconnectAttempts => _reconnectAttempts;
        public bool IsReconnecting => _isReconnecting;

        public ConnectionResilienceManager(ILogger<ConnectionResilienceManager> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Executes a connection attempt with exponential backoff.
        /// </summary>
        public async Task<T?> ExecuteWithReconnect<T>(
            Func<Task<T>> connectionFunc,
            string operationName,
            CancellationToken cancellationToken = default) where T : class
        {
            lock (_connectionLock)
            {
                if (_isReconnecting)
                {
                    _logger.LogDebug("[CONNECTION_RESILIENCE] Already reconnecting, skipping {Operation}", operationName);
                    return null;
                }
                _isReconnecting = true;
            }

            try
            {
                while (_reconnectAttempts < MAX_RECONNECT_ATTEMPTS && !cancellationToken.IsCancellationRequested)
                {
                    // Calculate backoff delay
                    var backoffMs = CalculateBackoff(_reconnectAttempts);
                    
                    if (_reconnectAttempts > 0)
                    {
                        _logger.LogInformation("[CONNECTION_RESILIENCE] Waiting {Delay}ms before attempt {Attempt}/{Max} for {Operation}",
                            backoffMs, _reconnectAttempts + 1, MAX_RECONNECT_ATTEMPTS, operationName);
                        
                        await Task.Delay(backoffMs, cancellationToken);
                    }

                    _lastConnectionAttempt = DateTime.UtcNow;
                    
                    try
                    {
                        _logger.LogDebug("[CONNECTION_RESILIENCE] Attempting {Operation} (attempt {Attempt})",
                            operationName, _reconnectAttempts + 1);
                        
                        var result = await connectionFunc();
                        
                        // Success - reset counters
                        lock (_connectionLock)
                        {
                            _reconnectAttempts = 0;
                            _lastSuccessfulConnection = DateTime.UtcNow;
                            _isReconnecting = false;
                        }
                        
                        _logger.LogInformation("[CONNECTION_RESILIENCE] {Operation} successful", operationName);
                        return result;
                    }
                    catch (Exception ex)
                    {
                        _reconnectAttempts++;
                        
                        if (_reconnectAttempts >= MAX_RECONNECT_ATTEMPTS)
                        {
                            _logger.LogError(ex, "[CONNECTION_RESILIENCE] {Operation} failed after {Attempts} attempts",
                                operationName, MAX_RECONNECT_ATTEMPTS);
                            throw;
                        }
                        
                        _logger.LogWarning(ex, "[CONNECTION_RESILIENCE] {Operation} failed on attempt {Attempt}, will retry",
                            operationName, _reconnectAttempts);
                    }
                }
                
                return null;
            }
            finally
            {
                lock (_connectionLock)
                {
                    _isReconnecting = false;
                }
            }
        }

        /// <summary>
        /// Marks a successful connection without executing.
        /// </summary>
        public void MarkConnectionSuccess()
        {
            lock (_connectionLock)
            {
                _reconnectAttempts = 0;
                _lastSuccessfulConnection = DateTime.UtcNow;
                _logger.LogDebug("[CONNECTION_RESILIENCE] Connection marked as successful");
            }
        }

        /// <summary>
        /// Marks a failed connection without executing.
        /// </summary>
        public void MarkConnectionFailure(string reason)
        {
            lock (_connectionLock)
            {
                _reconnectAttempts++;
                _logger.LogWarning("[CONNECTION_RESILIENCE] Connection marked as failed: {Reason} (attempt {Attempts})",
                    reason, _reconnectAttempts);
            }
        }

        /// <summary>
        /// Resets the connection state.
        /// </summary>
        public void Reset()
        {
            lock (_connectionLock)
            {
                _reconnectAttempts = 0;
                _isReconnecting = false;
                _logger.LogInformation("[CONNECTION_RESILIENCE] Connection state reset");
            }
        }

        /// <summary>
        /// Calculates exponential backoff delay.
        /// </summary>
        private int CalculateBackoff(int attempt)
        {
            if (attempt == 0) return 0;
            
            var backoff = INITIAL_BACKOFF_MS * Math.Pow(BACKOFF_MULTIPLIER, attempt - 1);
            return Math.Min((int)backoff, MAX_BACKOFF_MS);
        }

        /// <summary>
        /// Gets diagnostic information about the connection state.
        /// </summary>
        public string GetDiagnostics()
        {
            lock (_connectionLock)
            {
                var timeSinceLastSuccess = (DateTime.UtcNow - _lastSuccessfulConnection).TotalSeconds;
                var timeSinceLastAttempt = (DateTime.UtcNow - _lastConnectionAttempt).TotalSeconds;
                
                return $"Healthy: {IsHealthy}, Attempts: {_reconnectAttempts}, " +
                       $"LastSuccess: {timeSinceLastSuccess:F1}s ago, " +
                       $"LastAttempt: {timeSinceLastAttempt:F1}s ago, " +
                       $"Reconnecting: {_isReconnecting}";
            }
        }
    }
}