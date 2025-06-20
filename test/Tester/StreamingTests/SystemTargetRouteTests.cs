using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Forkleans.Providers;
using Forkleans.Streams;
using Forkleans.TestingHost;
using Forkleans.TestingHost.Utils;
using TestExtensions;
using UnitTests.GrainInterfaces;
using Xunit;

namespace Tester.StreamingTests
{
    public class SystemTargetRouteTests : OrleansTestingBase, IClassFixture<SystemTargetRouteTests.Fixture>
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
        private int eventsConsumed = 0;

        public class Fixture : BaseTestClusterFixture
        {
            public const string StreamProviderName = "MemoryStreamProvider";
            private const int partitionCount = 8;
            protected override void ConfigureTestCluster(TestClusterBuilder builder)
            {
                builder.Options.GatewayPerSilo = false;
                builder.AddSiloBuilderConfigurator<MySiloBuilderConfigurator>();
                builder.AddClientBuilderConfigurator<MyClientBuilderConfigurator>();
            }

            private class MyClientBuilderConfigurator : IClientBuilderConfigurator
            {
                public void Configure(IConfiguration configuration, IClientBuilder clientBuilder) => clientBuilder
                    .AddMemoryStreams<DefaultMemoryMessageBodySerializer>(StreamProviderName, b=>b
                    .ConfigurePartitioning(partitionCount));
            }

            private class MySiloBuilderConfigurator : ISiloConfigurator
            {
                public void Configure(ISiloBuilder hostBuilder) => hostBuilder
                    .AddMemoryGrainStorage("PubSubStore")
                    .AddMemoryStreams<DefaultMemoryMessageBodySerializer>(StreamProviderName, b=>b
                    .ConfigurePartitioning(partitionCount));
            }
        }

        private readonly Fixture fixture;

        public SystemTargetRouteTests(Fixture fixture)
        {
            this.fixture = fixture;
        }

        [SkippableFact(Skip = "https://github.com/dotnet/orleans/issues/4320"), TestCategory("Functional"), TestCategory("Streaming")]
        public async Task PersistentStreamingOverSingleGatewayTest()
        {
            const int streamCount = 100;

            this.fixture.Logger.LogInformation("************************ PersistentStreamingOverSingleGatewayTest *********************************");

            // generate stream Id's
            List<Guid> streamIds = Enumerable.Range(0, streamCount)
                .Select(i => Guid.NewGuid())
                .ToList();

            // subscribe to all streams
            foreach(Guid streamId in streamIds)
            {
                IStreamProvider streamProvider = this.fixture.Client.GetStreamProvider(Fixture.StreamProviderName);
                IAsyncObservable<int> stream = streamProvider.GetStream<int>(streamId);
                await stream.SubscribeAsync(OnNextAsync);
            }

            // create producer grains
            List<ISampleStreaming_ProducerGrain> producers = streamIds
                .Select(id => this.fixture.GrainFactory.GetGrain<ISampleStreaming_ProducerGrain>(id))
                .ToList();

            // become producers
            await Task.WhenAll(Enumerable.Range(0, streamCount).Select(i => producers[i].BecomeProducer(streamIds[i], null, Fixture.StreamProviderName)));

            // produce some events
            await Task.WhenAll(Enumerable.Range(0, streamCount).Select(i => producers[i].StartPeriodicProducing()));
            await Task.Delay(TimeSpan.FromMilliseconds(1000));
            await Task.WhenAll(Enumerable.Range(0, streamCount).Select(i => producers[i].StopPeriodicProducing()));

            int[] counts = await Task.WhenAll(Enumerable.Range(0, streamCount).Select(i => producers[i].GetNumberProduced()));

            // make sure all went well
            await TestingUtils.WaitUntilAsync(lastTry => CheckCounters(counts.Sum(), lastTry), Timeout);
        }

        private Task OnNextAsync(int e, StreamSequenceToken token)
        {
            Interlocked.Increment(ref this.eventsConsumed);
            return Task.CompletedTask;
        }

        private Task<bool> CheckCounters(int eventsProduced, bool assertIsTrue)
        {
            int numConsumed = this.eventsConsumed;
            if (!assertIsTrue) return Task.FromResult(eventsProduced == numConsumed);
            Assert.Equal(eventsProduced, numConsumed);
            return Task.FromResult(true);
        }
    }
}
