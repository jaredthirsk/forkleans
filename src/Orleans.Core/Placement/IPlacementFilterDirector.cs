using System.Collections.Generic;
using Forkleans.Runtime;
using Forkleans.Runtime.Placement;

#nullable enable
namespace Forkleans.Placement;

public interface IPlacementFilterDirector
{
    IEnumerable<SiloAddress> Filter(PlacementFilterStrategy filterStrategy, PlacementTarget target, IEnumerable<SiloAddress> silos);
}
