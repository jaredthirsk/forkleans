using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans;
using Orleans.Runtime;

namespace Granville.Rpc.Multiplexing
{
    /// <summary>
    /// Manages multiple RPC client connections and routes grain requests to appropriate servers.
    /// </summary>
    public interface IRpcClientMultiplexer : IDisposable
    {
        // Server management
        /// <summary>
        /// Registers a new server with the multiplexer.
        /// </summary>
        Task<bool> RegisterServerAsync(IServerDescriptor server);

        /// <summary>
        /// Unregisters a server from the multiplexer.
        /// </summary>
        Task<bool> UnregisterServerAsync(string serverId);

        /// <summary>
        /// Gets all registered servers.
        /// </summary>
        IReadOnlyDictionary<string, IServerDescriptor> GetRegisteredServers();
        
        // Grain operations - matches RpcClient interface
        /// <summary>
        /// Gets a grain reference with a GUID key.
        /// </summary>
        TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string? grainClassNamePrefix = null) 
            where TGrainInterface : IGrainWithGuidKey;

        /// <summary>
        /// Gets a grain reference with an integer key.
        /// </summary>
        TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string? grainClassNamePrefix = null) 
            where TGrainInterface : IGrainWithIntegerKey;

        /// <summary>
        /// Gets a grain reference with a string key.
        /// </summary>
        TGrainInterface GetGrain<TGrainInterface>(string primaryKey, string? grainClassNamePrefix = null) 
            where TGrainInterface : IGrainWithStringKey;

        /// <summary>
        /// Gets a grain reference with a GUID compound key.
        /// </summary>
        TGrainInterface GetGrain<TGrainInterface>(Guid primaryKey, string keyExtension, string? grainClassNamePrefix = null) 
            where TGrainInterface : IGrainWithGuidCompoundKey;

        /// <summary>
        /// Gets a grain reference with an integer compound key.
        /// </summary>
        TGrainInterface GetGrain<TGrainInterface>(long primaryKey, string keyExtension, string? grainClassNamePrefix = null) 
            where TGrainInterface : IGrainWithIntegerCompoundKey;

        /// <summary>
        /// Gets a grain reference by GrainId.
        /// </summary>
        TGrainInterface GetGrain<TGrainInterface>(GrainId grainId) 
            where TGrainInterface : IAddressable;

        /// <summary>
        /// Gets an untyped grain reference.
        /// </summary>
        IAddressable GetGrain(GrainId grainId);

        /// <summary>
        /// Gets an untyped grain reference with interface type.
        /// </summary>
        IAddressable GetGrain(GrainId grainId, GrainInterfaceType interfaceType);

        /// <summary>
        /// Gets a grain reference by type and GUID key.
        /// </summary>
        IGrain GetGrain(Type grainInterfaceType, Guid grainPrimaryKey);

        /// <summary>
        /// Gets a grain reference by type and integer key.
        /// </summary>
        IGrain GetGrain(Type grainInterfaceType, long grainPrimaryKey);

        /// <summary>
        /// Gets a grain reference by type and string key.
        /// </summary>
        IGrain GetGrain(Type grainInterfaceType, string grainPrimaryKey);
        
        // Context management
        /// <summary>
        /// Sets the routing context for grain requests.
        /// </summary>
        void SetRoutingContext(IRoutingContext context);

        /// <summary>
        /// Gets the current routing context.
        /// </summary>
        IRoutingContext GetRoutingContext();
        
        // Health monitoring
        /// <summary>
        /// Gets the health status of all registered servers.
        /// </summary>
        Task<Dictionary<string, ServerHealthStatus>> GetServerHealthAsync();

        /// <summary>
        /// Event raised when a server's health status changes.
        /// </summary>
        event EventHandler<ServerHealthChangedEventArgs> ServerHealthChanged;
    }

    /// <summary>
    /// Event arguments for server health status changes.
    /// </summary>
    public class ServerHealthChangedEventArgs : EventArgs
    {
        /// <summary>
        /// The server whose health changed.
        /// </summary>
        public string ServerId { get; set; }

        /// <summary>
        /// The previous health status.
        /// </summary>
        public ServerHealthStatus OldStatus { get; set; }

        /// <summary>
        /// The new health status.
        /// </summary>
        public ServerHealthStatus NewStatus { get; set; }

        /// <summary>
        /// Timestamp of the health change.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}