using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Forkleans.Streaming")]
[assembly: InternalsVisibleTo("Forkleans.Reminders")]
[assembly: InternalsVisibleTo("Forkleans.Journaling")]
[assembly: InternalsVisibleTo("Forkleans.TestingHost")]

[assembly: InternalsVisibleTo("AWSUtils.Tests")]
[assembly: InternalsVisibleTo("LoadTestGrains")]
[assembly: InternalsVisibleTo("NonSilo.Tests")]
[assembly: InternalsVisibleTo("Tester.AzureUtils")]
[assembly: InternalsVisibleTo("Tester.AdoNet")]
[assembly: InternalsVisibleTo("TesterInternal")]
[assembly: InternalsVisibleTo("TestInternalGrains")]
[assembly: InternalsVisibleTo("Benchmarks")]

// Mocking libraries
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]