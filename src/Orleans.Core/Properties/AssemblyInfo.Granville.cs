using System.Runtime.CompilerServices;

// Granville Orleans assemblies
[assembly: InternalsVisibleTo("Granville.Orleans.Runtime")]

// Granville RPC assemblies
[assembly: InternalsVisibleTo("Granville.Rpc.Server")]
[assembly: InternalsVisibleTo("Granville.Rpc.Client")]
[assembly: InternalsVisibleTo("Granville.Rpc.Abstractions")]

// Allow the Orleans.Core shim to forward internal types
[assembly: InternalsVisibleTo("Orleans.Core")]