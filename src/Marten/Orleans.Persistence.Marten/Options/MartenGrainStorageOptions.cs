using System;
using Newtonsoft.Json;
using Orleans.Persistence.AdoNet.Storage;
using Orleans.Runtime;
using Orleans.Storage;

namespace Orleans.Configuration
{
    /// <summary>
    /// Options for MartenGrainStorage
    /// </summary>
    public class MartenGrainStorageOptions : IStorageProviderSerializerOptions
    {
        /// <summary>
        /// Connection string for Marten storage.
        /// </summary>
        [Redact]
        public string ConnectionString { get; set; }

        /// <summary>
        /// Stage of silo lifecycle where storage should be initialized.  Storage must be initialized prior to use.
        /// </summary>
        public int InitStage { get; set; } = DEFAULT_INIT_STAGE;
        /// <summary>
        /// Default init stage in silo lifecycle.
        /// </summary>
        public const int DEFAULT_INIT_STAGE = ServiceLifecycleStage.ApplicationServices;
                
        /// <inheritdoc/>
        public IGrainStorageSerializer GrainStorageSerializer { get; set; }
    }

    /// <summary>
    /// ConfigurationValidator for MartenGrainStorageOptions
    /// </summary>
    public class MartenGrainStorageOptionsValidator : IConfigurationValidator
    {
        private readonly MartenGrainStorageOptions options;
        private readonly string name;
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="configurationOptions">The option to be validated.</param>
        /// <param name="name">The name of the option to be validated.</param>
        public MartenGrainStorageOptionsValidator(MartenGrainStorageOptions configurationOptions, string name)
        {
            if(configurationOptions == null)
                throw new OrleansConfigurationException($"Invalid MartenGrainStorageOptions for MartenGrainStorage {name}. Options is required.");
            this.options = configurationOptions;
            this.name = name;
        }
        /// <inheritdoc cref="IConfigurationValidator"/>
        public void ValidateConfiguration()
        {
            if (string.IsNullOrWhiteSpace(this.options.ConnectionString))
            {
                throw new OrleansConfigurationException($"Invalid {nameof(MartenGrainStorageOptions)} values for {nameof(MartenGrainStorage)} \"{name}\". {nameof(options.ConnectionString)} is required.");
            }
        }
    }
}
