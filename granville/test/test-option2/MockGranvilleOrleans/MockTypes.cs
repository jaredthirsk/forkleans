// Mock types to simulate Granville.Orleans.Core
// In reality, these would be the actual Orleans types built with Granville naming

using System;

namespace Orleans
{
    public interface IGrain
    {
        string GetGrainIdentity();
    }

    public interface IGrainFactory
    {
        T GetGrain<T>(long primaryKey) where T : IGrain;
    }

    namespace Runtime
    {
        public class GrainReference
        {
            public string GrainId { get; set; } = "MockGrainReference";
        }
    }

    namespace Configuration
    {
        public class ClusterOptions
        {
            public string ClusterId { get; set; } = "MockCluster";
            public string ServiceId { get; set; } = "MockService";
        }
    }
}

// Remove the DependencyInjection namespace to keep it simple

namespace Orleans.Hosting
{
    public interface ISiloBuilder
    {
        ISiloBuilder Configure<TOptions>(Action<TOptions> configureOptions) where TOptions : class, new();
    }

    // Mock extension for SignalR backplane
    public static class SignalRExtensions
    {
        public static ISiloBuilder UseSignalRBackplane(this ISiloBuilder builder)
        {
            Console.WriteLine("Mock UseSignalRBackplane called - This proves the redirect is working!");
            return builder;
        }
    }
}