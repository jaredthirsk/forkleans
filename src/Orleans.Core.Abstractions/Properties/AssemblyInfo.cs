using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Orleans.Core")]
[assembly: InternalsVisibleTo("Orleans.Runtime")]
[assembly: InternalsVisibleTo("Orleans.TestingHost")]
[assembly: InternalsVisibleTo("Orleans.Streaming")]
[assembly: InternalsVisibleTo("Orleans.Streaming.Abstractions")]
[assembly: InternalsVisibleTo("Orleans.Reminders")]

[assembly: InternalsVisibleTo("DefaultCluster.Tests")]
[assembly: InternalsVisibleTo("NonSilo.Tests")]
[assembly: InternalsVisibleTo("Tester.AzureUtils")]
[assembly: InternalsVisibleTo("AWSUtils.Tests")]
[assembly: InternalsVisibleTo("TesterInternal")]
[assembly: InternalsVisibleTo("TestInternalGrainInterfaces")]
[assembly: InternalsVisibleTo("TestInternalGrains")]

// Granville RPC assemblies
[assembly: InternalsVisibleTo("Granville.Rpc.Server")]
[assembly: InternalsVisibleTo("Granville.Rpc.Client")]
[assembly: InternalsVisibleTo("Granville.Rpc.Abstractions")]

// Mocking libraries
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]