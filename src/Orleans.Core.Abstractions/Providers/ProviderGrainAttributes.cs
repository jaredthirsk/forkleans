using System;

namespace Forkleans.Providers
{
    /// <summary>
    /// The [Forkleans.Providers.StorageProvider] attribute is used to define which storage provider to use for persistence of grain state.
    /// <para>
    /// Specifying [Forkleans.Providers.StorageProvider] property is recommended for all grains which extend Grain&lt;T&gt;.
    /// If no [Forkleans.Providers.StorageProvider] attribute is  specified, then a "Default" storage provider will be used.
    /// If a suitable storage provider cannot be located for this grain, then the grain will fail to load into the Silo.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class StorageProviderAttribute : Attribute
    {
        /// <summary>
        /// Gets or sets the name of the provider to be used for persisting of grain state.
        /// </summary>
        public string ProviderName { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="StorageProviderAttribute"/> class.
        /// </summary>
        public StorageProviderAttribute()
        {
            ProviderName = ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME;
        }
    }

    /// <summary>
    /// The [Forkleans.Providers.LogConsistencyProvider] attribute is used to define which consistency provider to use for grains using the log-view state abstraction.
    /// <para>
    /// Specifying [Forkleans.Providers.LogConsistencyProvider] property is recommended for all grains that derive
    /// from LogConsistentGrain, such as JournaledGrain.
    /// If no [Forkleans.Providers.LogConsistencyProvider] attribute is  specified, then the runtime tries to locate
    /// one as follows. First, it looks for a
    /// "Default" provider in the configuration file, then it checks if the grain type defines a default.
    /// If a consistency provider cannot be located for this grain, then the grain will fail to load into the Silo.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class LogConsistencyProviderAttribute : Attribute
    {
        /// <summary>
        /// Gets or sets name of the provider to be used for consistency.
        /// </summary>
        public string ProviderName { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="LogConsistencyProviderAttribute"/> class.
        /// </summary>
        public LogConsistencyProviderAttribute()
        {
            ProviderName = ProviderConstants.DEFAULT_LOG_CONSISTENCY_PROVIDER_NAME;
        }
    }
}
