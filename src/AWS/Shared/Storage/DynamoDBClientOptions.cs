#if CLUSTERING_DYNAMODB
namespace Forkleans.Clustering.DynamoDB
#elif PERSISTENCE_DYNAMODB
namespace Forkleans.Persistence.DynamoDB
#elif REMINDERS_DYNAMODB
namespace Forkleans.Reminders.DynamoDB
#else
// No default namespace intentionally to cause compile errors if something is not defined
#endif
{
    public class DynamoDBClientOptions
    {
        /// <summary>
        /// AccessKey string for DynamoDB Storage
        /// </summary>
        [Redact]
        public string AccessKey { get; set; }

        /// <summary>
        /// Secret key for DynamoDB storage
        /// </summary>
        [Redact]
        public string SecretKey { get; set; }

        /// <summary>
        /// DynamoDB region name, such as "us-west-2"
        /// </summary>
        public string Service { get; set; }

        /// <summary>
        /// Token for DynamoDB storage
        /// </summary>
        public string Token { get; set; }

        /// <summary>
        /// AWS profile name.
        /// </summary>
        public string ProfileName { get; set; }
    }
}