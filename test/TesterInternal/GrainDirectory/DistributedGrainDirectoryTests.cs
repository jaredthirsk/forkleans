#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Forkleans.GrainDirectory;
using Forkleans.Runtime.GrainDirectory;
using Forkleans.TestingHost;
using Tester.Directories;
using TestExtensions;
using Xunit;
using Xunit.Abstractions;

namespace UnitTests.GrainDirectory;

[TestCategory("BVT"), TestCategory("Directory")]
public sealed class DefaultGrainDirectoryTests(DefaultClusterFixture fixture, ITestOutputHelper output)
    : GrainDirectoryTests<IGrainDirectory>(output), IClassFixture<DefaultClusterFixture>
{
    private readonly TestCluster _testCluster = fixture.HostedCluster;
    private InProcessSiloHandle Primary => (InProcessSiloHandle)_testCluster.Primary;

    protected override IGrainDirectory CreateGrainDirectory() =>
        Primary.SiloHost.Services.GetRequiredService<GrainDirectoryResolver>().DefaultGrainDirectory;
}
