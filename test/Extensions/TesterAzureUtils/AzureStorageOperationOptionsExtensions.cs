using Azure.Core.Diagnostics;
using Azure.Data.Tables;
using Azure.Identity;
using TestExtensions;

namespace Tester.AzureUtils
{
    public static class AzureStorageOperationOptionsExtensions
    {
        public static DefaultAzureCredential Credential = new DefaultAzureCredential();

        public static Forkleans.Clustering.AzureStorage.AzureStorageOperationOptions ConfigureTestDefaults(this Forkleans.Clustering.AzureStorage.AzureStorageOperationOptions options)
        {
            options.TableServiceClient = GetTableServiceClient();

            return options;
        }

        public static TableServiceClient GetTableServiceClient()
        {
            return TestDefaultConfiguration.UseAadAuthentication
                ? new(TestDefaultConfiguration.TableEndpoint, TestDefaultConfiguration.TokenCredential)
                : new(TestDefaultConfiguration.DataConnectionString);
        }

        public static Forkleans.GrainDirectory.AzureStorage.AzureStorageOperationOptions ConfigureTestDefaults(this Forkleans.GrainDirectory.AzureStorage.AzureStorageOperationOptions options)
        {
            options.TableServiceClient = GetTableServiceClient();

            return options;
        }

        public static Forkleans.Persistence.AzureStorage.AzureStorageOperationOptions ConfigureTestDefaults(this Forkleans.Persistence.AzureStorage.AzureStorageOperationOptions options)
        {
            options.TableServiceClient = GetTableServiceClient();

            return options;
        }

        public static Forkleans.Reminders.AzureStorage.AzureStorageOperationOptions ConfigureTestDefaults(this Forkleans.Reminders.AzureStorage.AzureStorageOperationOptions options)
        {
            options.TableServiceClient = GetTableServiceClient();

            return options;
        }

        public static Forkleans.Configuration.AzureBlobStorageOptions ConfigureTestDefaults(this Forkleans.Configuration.AzureBlobStorageOptions options)
        {
            if (TestDefaultConfiguration.UseAadAuthentication)
            {
                options.BlobServiceClient = new(TestDefaultConfiguration.DataBlobUri, TestDefaultConfiguration.TokenCredential);
            }
            else
            {
                options.BlobServiceClient = new(TestDefaultConfiguration.DataConnectionString);
            }

            return options;
        }

        public static Forkleans.Configuration.AzureQueueOptions ConfigureTestDefaults(this Forkleans.Configuration.AzureQueueOptions options)
        {
            if (TestDefaultConfiguration.UseAadAuthentication)
            {
                options.QueueServiceClient = new(TestDefaultConfiguration.DataQueueUri, TestDefaultConfiguration.TokenCredential);
            }
            else
            {
                options.QueueServiceClient = new(TestDefaultConfiguration.DataConnectionString);
            }

            return options;
        }

        public static Forkleans.Configuration.AzureBlobLeaseProviderOptions ConfigureTestDefaults(this Forkleans.Configuration.AzureBlobLeaseProviderOptions options)
        {
            if (TestDefaultConfiguration.UseAadAuthentication)
            {
                options.BlobServiceClient = new(TestDefaultConfiguration.DataBlobUri, TestDefaultConfiguration.TokenCredential);
            }
            else
            {
                options.BlobServiceClient = new(TestDefaultConfiguration.DataConnectionString);
            }

            return options;
        }
    }
}