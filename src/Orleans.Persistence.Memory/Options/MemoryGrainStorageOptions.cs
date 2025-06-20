using Forkleans.Runtime;
using Forkleans.Storage;

namespace Forkleans.Configuration
{
    /// <summary>
    /// Options for MemoryGrainStorage
    /// </summary>
    public class MemoryGrainStorageOptions : IStorageProviderSerializerOptions
    {
        /// <summary>
        /// Default number of queue storage grains.
        /// </summary>
        public const int NumStorageGrainsDefaultValue = 10;

        /// <summary>
        /// Gets or sets the number of store grains to use.
        /// </summary>
        public int NumStorageGrains { get; set; } = NumStorageGrainsDefaultValue;

        /// <summary>
        /// Gets or sets the stage of silo lifecycle where storage should be initialized.  Storage must be initialized prior to use.
        /// </summary>
        public int InitStage { get; set; } = DEFAULT_INIT_STAGE;

        /// <inheritdoc/>
        public IGrainStorageSerializer GrainStorageSerializer { get; set; }

        /// <summary>
        /// Default init stage
        /// </summary>
        public const int DEFAULT_INIT_STAGE = ServiceLifecycleStage.ApplicationServices;
    }

    /// <summary>
    /// Validates <see cref="MemoryGrainStorageOptions"/>.
    /// </summary>
    public class MemoryGrainStorageOptionsValidator : IConfigurationValidator
    {
        private readonly MemoryGrainStorageOptions options;
        private readonly string name;

        /// <summary>
        /// Initializes a new instance of the <see cref="MemoryGrainStorageOptionsValidator"/> class.
        /// </summary>
        /// <param name="options">The options.</param>
        /// <param name="name">The name.</param>
        public MemoryGrainStorageOptionsValidator(MemoryGrainStorageOptions options, string name)
        {
            this.options = options;
            this.name = name;
        }
        
        /// <inheritdoc/>
        public void ValidateConfiguration()
        {
            if (this.options.NumStorageGrains <= 0)
                throw new ForkleansConfigurationException(
                    $"Configuration for {nameof(MemoryGrainStorage)} {name} is invalid. {nameof(this.options.NumStorageGrains)} must be larger than 0.");
            if(this.options.InitStage < ServiceLifecycleStage.RuntimeGrainServices)
                throw new ForkleansConfigurationException(
                   $"Configuration for {nameof(MemoryGrainStorage)} {name} is invalid. {nameof(this.options.InitStage)} must be larger than {ServiceLifecycleStage.RuntimeGrainServices} since " +
                   $"{nameof(MemoryGrainStorage)} depends on {nameof(MemoryStorageGrain)} to have grain environment to finish set up.");
        }
    }
}
