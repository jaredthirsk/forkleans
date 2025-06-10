using Newtonsoft.Json;

#if ORLEANS_CLUSTERING
namespace Forkleans.Clustering.Cosmos;
#elif ORLEANS_PERSISTENCE
namespace Forkleans.Persistence.Cosmos;
#elif ORLEANS_REMINDERS
namespace Forkleans.Reminders.Cosmos;
#elif ORLEANS_STREAMING
namespace Forkleans.Streaming.Cosmos;
#elif ORLEANS_DIRECTORY
namespace Forkleans.GrainDirectory.Cosmos;
#else
// No default namespace intentionally to cause compile errors if something is not defined
#endif

internal abstract class BaseEntity
{
    internal const string ID_FIELD = "id";
    internal const string ETAG_FIELD = "_etag";    

    [JsonProperty(ID_FIELD)]
    [JsonPropertyName(ID_FIELD)]
    public string Id { get; set; } = default!;

    [JsonProperty(ETAG_FIELD)]
    [JsonPropertyName(ETAG_FIELD)]
    public string ETag { get; set; } = default!;
}