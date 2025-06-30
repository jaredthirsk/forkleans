using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Orleans.BroadcastChannel")]
[assembly: InternalsVisibleTo("Orleans.CodeGeneration")]
[assembly: InternalsVisibleTo("Orleans.CodeGeneration.Build")]
[assembly: InternalsVisibleTo("Orleans.Runtime")]
[assembly: InternalsVisibleTo("Orleans.Streaming")]
[assembly: InternalsVisibleTo("Orleans.TestingHost")]

[assembly: InternalsVisibleTo("AWSUtils.Tests")]
[assembly: InternalsVisibleTo("DefaultCluster.Tests")]
[assembly: InternalsVisibleTo("GoogleUtils.Tests")]
[assembly: InternalsVisibleTo("LoadTestGrains")]
[assembly: InternalsVisibleTo("NonSilo.Tests")]
[assembly: InternalsVisibleTo("Benchmarks")]
[assembly: InternalsVisibleTo("Tester")]
[assembly: InternalsVisibleTo("Tester.AzureUtils")]
[assembly: InternalsVisibleTo("Tester.Cosmos")]
[assembly: InternalsVisibleTo("Tester.AdoNet")]
[assembly: InternalsVisibleTo("Tester.Redis")]
[assembly: InternalsVisibleTo("Tester.ZooKeeperUtils")]
[assembly: InternalsVisibleTo("TesterInternal")]
[assembly: InternalsVisibleTo("TestExtensions")]
[assembly: InternalsVisibleTo("TestInternalGrains")]
[assembly: InternalsVisibleTo("CodeGenerator.Tests")]

[assembly: InternalsVisibleTo("Orleans.Reminders")]

// Granville RPC assemblies
[assembly: InternalsVisibleTo("Granville.Rpc.Server")]
[assembly: InternalsVisibleTo("Granville.Rpc.Client")]
[assembly: InternalsVisibleTo("Granville.Rpc.Abstractions")]

// Mocking libraries
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
