using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Forkleans.Core")]
[assembly: InternalsVisibleTo("Forkleans.Runtime")]
[assembly: InternalsVisibleTo("Forkleans.TestingHost")]
[assembly: InternalsVisibleTo("Forkleans.Streaming")]
[assembly: InternalsVisibleTo("Forkleans.Streaming.Abstractions")]
[assembly: InternalsVisibleTo("Forkleans.Reminders")]

[assembly: InternalsVisibleTo("DefaultCluster.Tests")]
[assembly: InternalsVisibleTo("NonSilo.Tests")]
[assembly: InternalsVisibleTo("Tester.AzureUtils")]
[assembly: InternalsVisibleTo("AWSUtils.Tests")]
[assembly: InternalsVisibleTo("TesterInternal")]
[assembly: InternalsVisibleTo("TestInternalGrainInterfaces")]
[assembly: InternalsVisibleTo("TestInternalGrains")]

// Mocking libraries
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
