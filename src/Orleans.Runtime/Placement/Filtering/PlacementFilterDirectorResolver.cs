using System;
using Microsoft.Extensions.DependencyInjection;
using Forkleans.Placement;

#nullable enable
namespace Forkleans.Runtime.Placement.Filtering;

/// <summary>
/// Responsible for resolving an <see cref="IPlacementFilterDirector"/> for a <see cref="PlacementFilterStrategy"/>.
/// </summary>
public sealed class PlacementFilterDirectorResolver(IServiceProvider services)
{
    public IPlacementFilterDirector GetFilterDirector(PlacementFilterStrategy placementFilterStrategy) => services.GetRequiredKeyedService<IPlacementFilterDirector>(placementFilterStrategy.GetType());
}