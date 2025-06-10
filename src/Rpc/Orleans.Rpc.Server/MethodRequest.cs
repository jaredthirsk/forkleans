using System;
using Forkleans;
using Forkleans.Runtime;

namespace Forkleans.Rpc
{
    /// <summary>
    /// Simple method request for RPC invocation.
    /// </summary>
    internal class MethodRequest
    {
        public GrainInterfaceType InterfaceType { get; set; }
        public int MethodId { get; set; }
        public object[] Arguments { get; set; }
    }
}