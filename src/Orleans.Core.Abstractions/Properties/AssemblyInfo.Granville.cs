using System.Runtime.CompilerServices;

// Granville Orleans assemblies (for compatibility when building as Granville.*)
[assembly: InternalsVisibleTo("Granville.Orleans.Core")]
[assembly: InternalsVisibleTo("Granville.Orleans.Runtime")]
[assembly: InternalsVisibleTo("Granville.Orleans.TestingHost")]
[assembly: InternalsVisibleTo("Granville.Orleans.Streaming")]
[assembly: InternalsVisibleTo("Granville.Orleans.Streaming.Abstractions")]
[assembly: InternalsVisibleTo("Granville.Orleans.Reminders")]

// Granville RPC assemblies
[assembly: InternalsVisibleTo("Granville.Rpc.Server")]
[assembly: InternalsVisibleTo("Granville.Rpc.Client")]
[assembly: InternalsVisibleTo("Granville.Rpc.Abstractions")]