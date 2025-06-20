using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Forkleans.Configuration;
using Forkleans.Persistence;
using Forkleans.Providers;
using Forkleans.Runtime;
using Forkleans.Serialization;
using Forkleans.Serialization.Serializers;
using Forkleans.Storage;
using StackExchange.Redis;
using Tester;
using TestExtensions;

public class CommonFixture : TestEnvironmentFixture
{
    /// <summary>
    /// Caches DefaultProviderRuntime for multiple uses.
    /// </summary>
    private IProviderRuntime DefaultProviderRuntime { get; }

    /// <summary>
    /// Constructor.
    /// </summary>
    public CommonFixture()
    {
        _ = this.Services.GetRequiredService<IOptions<ClusterOptions>>();
        DefaultProviderRuntime = new ClientProviderRuntime(
            this.InternalGrainFactory,
            this.Services,
            this.Services.GetRequiredService<ClientGrainContext>());
    }

    /// <summary>
    /// Returns a correct implementation of the persistence provider according to environment variables.
    /// </summary>
    /// <remarks>If the environment invariants have failed to hold upon creation of the storage provider,
    /// a <em>null</em> value will be provided.</remarks>
    public async Task<IGrainStorage> CreateRedisGrainStorage(bool useOrleansSerializer = false, bool deleteStateOnClear = false)
    {
        TestUtils.CheckForRedis();
        IGrainStorageSerializer grainStorageSerializer = useOrleansSerializer ? new ForkleansGrainStorageSerializer(this.DefaultProviderRuntime.ServiceProvider.GetService<Serializer>())
                                                                              : new JsonGrainStorageSerializer(this.DefaultProviderRuntime.ServiceProvider.GetService<ForkleansJsonSerializer>());
        var options = new RedisStorageOptions()
        {
            ConfigurationOptions = ConfigurationOptions.Parse(TestDefaultConfiguration.RedisConnectionString),
            GrainStorageSerializer = grainStorageSerializer,
            DeleteStateOnClear = deleteStateOnClear,
        };

        var clusterOptions = new ClusterOptions()
        {
            ServiceId = Guid.NewGuid().ToString()
        };

        var serviceProvider = DefaultProviderRuntime.ServiceProvider;
        var storageProvider = new RedisGrainStorage(
            string.Empty,
            options,
            grainStorageSerializer,
            Options.Create(clusterOptions),
            serviceProvider.GetRequiredService<IActivatorProvider>(),
            serviceProvider.GetRequiredService<ILogger<RedisGrainStorage>>());
        ISiloLifecycleSubject siloLifeCycle = new SiloLifecycleSubject(NullLoggerFactory.Instance.CreateLogger<SiloLifecycleSubject>());
        storageProvider.Participate(siloLifeCycle);
        await siloLifeCycle.OnStart(CancellationToken.None);
        return storageProvider;
    }
}