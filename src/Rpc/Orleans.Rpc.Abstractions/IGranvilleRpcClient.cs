using System;
using System.Threading.Tasks;
using Orleans.Rpc.Transport;

namespace Orleans.Rpc
{
    /// <summary>
    /// Main client interface for Granville RPC with multiple abstraction levels.
    /// </summary>
    public interface IGranvilleRpcClient : IDisposable
    {
        /// <summary>
        /// Connects to a Granville RPC server.
        /// </summary>
        Task ConnectAsync(string host, int port);
        
        /// <summary>
        /// Disconnects from the server.
        /// </summary>
        Task DisconnectAsync();
        
        /// <summary>
        /// Invokes an RPC method on the server (full RPC stack, ~1ms overhead).
        /// </summary>
        Task<TResult> InvokeAsync<TService, TResult>(Func<TService, Task<TResult>> method);
        
        /// <summary>
        /// Invokes an RPC method on the server without a return value.
        /// </summary>
        Task InvokeAsync<TService>(Func<TService, Task> method);
        
        /// <summary>
        /// Gets the bypass API for lower-level message sending (~0.3ms overhead).
        /// </summary>
        IGranvilleBypass Bypass { get; }
        
        /// <summary>
        /// Gets direct transport access for zero-overhead hot paths (0ms overhead).
        /// WARNING: Bypasses all Granville abstractions. Use with caution.
        /// </summary>
        IDirectTransportAccess DirectAccess { get; }
        
        /// <summary>
        /// Gets the connection state.
        /// </summary>
        bool IsConnected { get; }
        
        /// <summary>
        /// Gets performance metrics for the connection.
        /// </summary>
        IRpcMetrics Metrics { get; }
    }
    
    /// <summary>
    /// Performance metrics for RPC operations.
    /// </summary>
    public interface IRpcMetrics
    {
        /// <summary>
        /// Average round-trip time for RPC calls in milliseconds.
        /// </summary>
        double AverageRttMs { get; }
        
        /// <summary>
        /// Number of messages sent.
        /// </summary>
        long MessagesSent { get; }
        
        /// <summary>
        /// Number of messages received.
        /// </summary>
        long MessagesReceived { get; }
        
        /// <summary>
        /// Number of failed sends.
        /// </summary>
        long FailedSends { get; }
    }
}