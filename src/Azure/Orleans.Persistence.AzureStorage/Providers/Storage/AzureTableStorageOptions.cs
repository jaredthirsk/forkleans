using Forkleans.Persistence.AzureStorage;
using Forkleans.Storage;

namespace Forkleans.Configuration
{
    /// <summary>
    /// Configuration for AzureTableGrainStorage
    /// </summary>
    public class AzureTableStorageOptions : AzureStorageOperationOptions, IStorageProviderSerializerOptions
    {
        /// <summary>
        /// Table name where grain stage is stored
        /// </summary>
        public override string TableName { get; set; } = DEFAULT_TABLE_NAME;
        public const string DEFAULT_TABLE_NAME = "ForkleansGrainState";

        /// <summary>
        /// Indicates if grain data should be deleted or reset to defaults when a grain clears it's state.
        /// </summary>
        public bool DeleteStateOnClear { get; set; } = false;

        /// <summary>
        /// Indicates if grain data should be stored in string or in binary format.
        /// </summary>
        public bool UseStringFormat { get; set; }

        /// <summary>
        /// Stage of silo lifecycle where storage should be initialized.  Storage must be initialized prior to use.
        /// </summary>
        public int InitStage { get; set; } = DEFAULT_INIT_STAGE;
        public const int DEFAULT_INIT_STAGE = ServiceLifecycleStage.ApplicationServices;

        /// <inheritdoc/>
        public IGrainStorageSerializer GrainStorageSerializer { get; set; }
    }

    /// <summary>
    /// Configuration validator for AzureTableStorageOptions
    /// </summary>
    public class AzureTableGrainStorageOptionsValidator : AzureStorageOperationOptionsValidator<AzureTableStorageOptions>
    {
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="options">The option to be validated.</param>
        /// <param name="name">The option name to be validated.</param>
        public AzureTableGrainStorageOptionsValidator(AzureTableStorageOptions options, string name) : base(options, name)
        {
        }
    }
}
