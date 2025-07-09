using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Orleans.BroadcastChannel")]
[assembly: InternalsVisibleTo("Orleans.CodeGeneration")]
[assembly: InternalsVisibleTo("Orleans.CodeGeneration.Build")]
[assembly: InternalsVisibleTo("Orleans.Runtime")]
[assembly: InternalsVisibleTo("Orleans.Streaming")]
[assembly: InternalsVisibleTo("Orleans.TestingHost")]

// Granville versions
[assembly: InternalsVisibleTo("Granville.Orleans.BroadcastChannel")]
[assembly: InternalsVisibleTo("Granville.Orleans.CodeGeneration")]
[assembly: InternalsVisibleTo("Granville.Orleans.CodeGeneration.Build")]
[assembly: InternalsVisibleTo("Granville.Orleans.Runtime")]
[assembly: InternalsVisibleTo("Granville.Orleans.Streaming")]
[assembly: InternalsVisibleTo("Granville.Orleans.TestingHost")]

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
[assembly: InternalsVisibleTo("Granville.Orleans.Reminders")]

// Shim assemblies for type forwarding
[assembly: InternalsVisibleTo("Orleans.Core")]

// Mocking libraries
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
