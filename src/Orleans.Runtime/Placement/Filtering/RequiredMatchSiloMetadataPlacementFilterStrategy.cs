using System;
using System.Collections.Generic;
using Forkleans.Metadata;
using Forkleans.Placement;

#nullable enable
namespace Forkleans.Runtime.Placement.Filtering;

public class RequiredMatchSiloMetadataPlacementFilterStrategy(string[] metadataKeys, int order)
    : PlacementFilterStrategy(order)
{
    public string[] MetadataKeys { get; private set; } = metadataKeys;

    public RequiredMatchSiloMetadataPlacementFilterStrategy() : this([], 0)
    {
    }

    public override void AdditionalInitialize(GrainProperties properties)
    {
        var placementFilterGrainProperty = GetPlacementFilterGrainProperty("metadata-keys", properties);
        if (placementFilterGrainProperty is null)
        {
            throw new ArgumentException("Invalid metadata-keys property value.");
        }
        MetadataKeys = placementFilterGrainProperty.Split(",");
    }

    protected override IEnumerable<KeyValuePair<string, string>> GetAdditionalGrainProperties(IServiceProvider services, Type grainClass, GrainType grainType,
        IReadOnlyDictionary<string, string> existingProperties)
    {
        yield return new KeyValuePair<string, string>("metadata-keys", String.Join(",", MetadataKeys));
    }
}