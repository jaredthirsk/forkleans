using System;
using System.Threading.Tasks;
using Orleans;

namespace Granville.Rpc
{
    /// <summary>
    /// RPC client interface.
    /// </summary>
    public interface IRpcClient : IClusterClient
    {
        /// <summary>
        /// Waits for the manifest to be populated from at least one server.
        /// </summary>
        /// <param name="timeout">The maximum time to wait. Default is 10 seconds.</param>
        /// <returns>A task that completes when the manifest is ready.</returns>
        /// <exception cref="TimeoutException">Thrown if the manifest is not populated within the timeout.</exception>
        Task WaitForManifestAsync(TimeSpan timeout = default);
    }
}