using System;
using Orleans;
using Orleans.Runtime;

namespace Granville.Rpc
{
    /// <summary>
    /// Simple method request for RPC invocation.
    /// </summary>
    internal class MethodRequest
    {
        public GrainInterfaceType InterfaceType { get; set; } = default!;
        public int MethodId { get; set; }
        public object[] Arguments { get; set; } = Array.Empty<object>();
    }
}