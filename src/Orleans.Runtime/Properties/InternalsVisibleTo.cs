using System.Runtime.CompilerServices;

// TODO: Fork cleanup - Move into AssemblyInfo.cs

// Allow RPC projects to access internal types
[assembly: InternalsVisibleTo("Forkleans.Rpc.Server")]
[assembly: InternalsVisibleTo("Forkleans.Rpc.Client")]
[assembly: InternalsVisibleTo("Forkleans.Rpc.Transport.LiteNetLib")]
[assembly: InternalsVisibleTo("Forkleans.Rpc.Transport.Ruffles")]

// Test projects
[assembly: InternalsVisibleTo("Forkleans.Rpc.Tests")]
[assembly: InternalsVisibleTo("Forkleans.Rpc.Server.Tests")]
[assembly: InternalsVisibleTo("Forkleans.Rpc.Client.Tests")]
