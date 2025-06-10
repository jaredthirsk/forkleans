using Newtonsoft.Json;

namespace Forkleans.Clustering.Cosmos;

internal abstract class BaseClusterEntity : BaseEntity
{
    [JsonProperty(nameof(ClusterId))]
    [JsonPropertyName(nameof(ClusterId))]
    public string ClusterId { get; set; } = default!;

    [JsonProperty(nameof(EntityType))]
    [JsonPropertyName(nameof(EntityType))]
    public abstract string EntityType { get; }
}