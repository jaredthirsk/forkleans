using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Forkleans.GrainDirectory;
using Forkleans.Runtime;
using Forkleans.Runtime.GrainDirectory;

namespace Forkleans.Rpc
{
    /// <summary>
    /// Simplified activation directory for RPC server.
    /// All activations are local, no distribution needed.
    /// </summary>
    internal sealed class RpcActivationDirectory
    {
        private readonly ILogger<RpcActivationDirectory> _logger;
        private readonly RpcCatalog _catalog;

        public RpcActivationDirectory(
            ILogger<RpcActivationDirectory> logger,
            RpcCatalog catalog)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        /// <summary>
        /// Finds the activation for a grain. In RPC mode, all activations are local.
        /// </summary>
        public ValueTask<GrainAddress> FindTargetActivation(GrainId grainId)
        {
            // In RPC mode, we always create local activations
            var activationId = ActivationId.NewId();
            var address = new GrainAddress
            {
                GrainId = grainId,
                ActivationId = activationId,
                SiloAddress = SiloAddress.Zero // Local RPC server
            };
            
            return new ValueTask<GrainAddress>(address);
        }

        /// <summary>
        /// Registers a new activation. In RPC mode, this is a local operation only.
        /// </summary>
        public ValueTask RegisterActivation(GrainAddress address)
        {
            _logger.LogDebug("Registered activation for grain {GrainId} with activation {ActivationId}", 
                address.GrainId, address.ActivationId);
            
            // In RPC mode, we don't need to register with a distributed directory
            return default;
        }

        /// <summary>
        /// Unregisters an activation. In RPC mode, this is a local operation only.
        /// </summary>
        public ValueTask UnregisterActivation(GrainAddress address)
        {
            _logger.LogDebug("Unregistered activation for grain {GrainId} with activation {ActivationId}", 
                address.GrainId, address.ActivationId);
            
            // In RPC mode, we don't need to unregister from a distributed directory
            return default;
        }

        /// <summary>
        /// Gets all activations. In RPC mode, returns all local activations.
        /// </summary>
        public IEnumerable<(GrainId GrainId, ActivationId ActivationId, SiloAddress SiloAddress)> GetAllActivations()
        {
            // Return all local activations
            // Note: This would need to be implemented based on the catalog's internal structure
            return Array.Empty<(GrainId, ActivationId, SiloAddress)>();
        }

        /// <summary>
        /// Attempts to retrieve an existing activation. Always returns false in RPC mode.
        /// </summary>
        public bool TryGetActivation(GrainId grainId, out GrainAddress address)
        {
            // In RPC mode, we don't cache activations in the directory
            address = default;
            return false;
        }
    }
}