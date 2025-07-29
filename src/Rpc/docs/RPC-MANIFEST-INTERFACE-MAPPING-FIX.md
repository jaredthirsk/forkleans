# RPC Manifest Interface Mapping Fix

## Issue
The RPC client was timing out when calling `GetGrain` despite receiving a valid manifest from the server with 10 grains during handshake. The issue was in how the `MultiServerManifestProvider` was building interface-to-grain mappings.

## Root Cause
Orleans' `GrainInterfaceTypeToGrainTypeResolver` expects grain properties to follow a specific convention for interface mappings:
- Keys should be: `"interface.0"`, `"interface.1"`, `"interface.2"`, etc.
- Values should be the interface type ID (e.g., `"Shooter.Grains.IPlayerGrain"`)

The bug was in `MultiServerManifestProvider.BuildClusterManifest()` which was creating keys like:
- `"interface.Shooter.Grains.IPlayerGrain"` (incorrect - interface type in the key)

Instead of:
- `"interface.0"` with value `"Shooter.Grains.IPlayerGrain"` (correct)

## Solution
Modified `MultiServerManifestProvider.cs` to:
1. Group interfaces by grain type first
2. Assign sequential numeric indices (0, 1, 2...) to each interface for a grain
3. Create properties with the correct key format: `"{WellKnownGrainTypeProperties.ImplementedInterfacePrefix}{counter}"`

## Code Changes
The fix involves restructuring how interface-to-grain mappings are processed:

```csharp
// Group interfaces by grain type to assign sequential indices
var grainInterfaces = new Dictionary<string, List<string>>();
foreach (var mapping in grainManifest.InterfaceToGrainMappings)
{
    if (!grainInterfaces.ContainsKey(mapping.Value))
    {
        grainInterfaces[mapping.Value] = new List<string>();
    }
    grainInterfaces[mapping.Value].Add(mapping.Key);
}

// Now add interface properties with numeric indices
foreach (var grainGroup in grainInterfaces)
{
    var grainType = GrainType.Create(grainGroup.Key);
    var counter = 0;
    
    // ... existing property handling ...
    
    // Add each interface with a numeric index
    foreach (var interfaceTypeStr in grainGroup.Value)
    {
        var interfaceType = GrainInterfaceType.Create(interfaceTypeStr);
        var key = $"{WellKnownGrainTypeProperties.ImplementedInterfacePrefix}{counter}";
        props = props.Add(key, interfaceType.ToString());
        counter++;
    }
}
```

## Impact
This fix ensures that:
1. The manifest provider correctly formats interface mappings
2. `GrainInterfaceTypeToGrainTypeResolver` can find grain implementations for interfaces
3. `GetGrain` calls succeed instead of timing out

## Testing
After applying this fix:
1. The server still sends the manifest with 10 grains during handshake
2. The client correctly processes the manifest
3. `GetGrain` calls resolve to the proper grain implementation
4. RPC calls to grains succeed