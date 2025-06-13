using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var redis = builder.AddRedis("orleans-redis");

var orleans = builder.AddOrleans("cluster")
    .WithClustering(redis);

builder.AddProject<DashboardToy_Frontend>("frontend")
    .WithReference(orleans)
    .WaitFor(redis)
    .WithReplicas(5);

builder.Build().Run();

/* Jared tried adding this to get build working. Not sure why Forkleans isn't found

namespace Forkleans
{
    public class Dummy { }
}
namespace Forkleans.Hosting
{
    public class Dummy { }
}

namespace Forkleans.Runtime
{
    public class Dummy { }
}

*/
