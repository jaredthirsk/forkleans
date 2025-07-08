using System.Runtime.CompilerServices;

// Granville RPC assemblies
[assembly: InternalsVisibleTo("Granville.Rpc.Server")]
[assembly: InternalsVisibleTo("Granville.Rpc.Client")]

// Allow the Orleans.Runtime shim to forward internal types
[assembly: InternalsVisibleTo("Orleans.Runtime")]