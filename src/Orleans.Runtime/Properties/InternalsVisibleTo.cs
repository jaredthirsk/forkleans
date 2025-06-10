using System.Runtime.CompilerServices;

// TODO: Fork cleanup - Move into AssemblyInfo.cs

// Allow RPC projects to access internal types
[assembly: InternalsVisibleTo("Orleans.Rpc.Server")]
[assembly: InternalsVisibleTo("Orleans.Rpc.Client")]
[assembly: InternalsVisibleTo("Orleans.Rpc.Transport.LiteNetLib")]
[assembly: InternalsVisibleTo("Orleans.Rpc.Transport.Ruffles")]

// Test projects
[assembly: InternalsVisibleTo("Orleans.Rpc.Tests")]
[assembly: InternalsVisibleTo("Orleans.Rpc.Server.Tests")]
[assembly: InternalsVisibleTo("Orleans.Rpc.Client.Tests")]
