using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Orleans.Streaming")]
[assembly: InternalsVisibleTo("Orleans.Reminders")]
[assembly: InternalsVisibleTo("Orleans.TestingHost")]

[assembly: InternalsVisibleTo("AWSUtils.Tests")]
[assembly: InternalsVisibleTo("LoadTestGrains")]
[assembly: InternalsVisibleTo("NonSilo.Tests")]
[assembly: InternalsVisibleTo("Tester.AzureUtils")]
[assembly: InternalsVisibleTo("Tester.AdoNet")]
[assembly: InternalsVisibleTo("TesterInternal")]
[assembly: InternalsVisibleTo("TestInternalGrains")]
[assembly: InternalsVisibleTo("Benchmarks")]

// Granville RPC assemblies
[assembly: InternalsVisibleTo("Granville.Rpc.Server")]
[assembly: InternalsVisibleTo("Granville.Rpc.Client")]

// Mocking libraries
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]