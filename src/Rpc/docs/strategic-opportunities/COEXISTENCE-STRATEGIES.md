# Coexistence Strategies for Granville RPC with Orleans

## The Challenge

When integrating Granville RPC with Orleans, we face a fundamental challenge:
- UFX.Orleans.SignalRBackplane → expects Microsoft.Orleans.Core types
- Granville.Rpc.* → built against modified Orleans.Core (with InternalsVisibleTo)
- Multiple assemblies loading different versions of "Orleans.Core"

This creates assembly resolution conflicts that must be resolved for successful coexistence.

## Option 1: Type Forwarding Shim

Create a Microsoft.Orleans.Core.dll that forwards all types to Granville.Orleans.Core.dll:

```csharp
[assembly: TypeForwardedTo(typeof(Orleans.IGrain))]
[assembly: TypeForwardedTo(typeof(Orleans.Runtime.GrainReference))]
// ... hundreds more
```

**Pros:**
- UFX SignalR and other third-party packages work unchanged
- Clean separation of concerns

**Cons:**
- Maintaining the forwarding list is tedious
- Must be updated when Orleans adds new public types
- Potential versioning conflicts

## Option 2: Assembly Binding Redirects

Use app.config/runtime binding redirects:

```xml
<runtime>
  <assemblyBinding>
    <dependentAssembly>
      <assemblyIdentity name="Orleans.Core" publicKeyToken="..." />
      <bindingRedirect oldVersion="0.0.0.0-9.1.2.0" newVersion="9.1.2.0" />
      <codeBase version="9.1.2.0" href="Granville.Orleans.Core.dll" />
    </dependentAssembly>
  </assemblyBinding>
</runtime>
```

**Pros:**
- No type forwarding needed
- Works at runtime

**Cons:**
- Not great for NuGet distribution
- Complex configuration

## Option 3: ILRepack/ILMerge Approach

Merge your InternalsVisibleTo changes directly into copies of the official assemblies:

```
ILRepack /out:Granville.Orleans.Core.dll Microsoft.Orleans.Core.dll YourInternalsVisibleToPatcher.dll
```

**Pros:**
- No type forwarding needed
- UFX SignalR works directly

**Cons:**
- Legal/licensing concerns
- Complex build process

## Option 4: Strategic Package Naming (Recommended)

Keep the Microsoft.Orleans.* package names but version them differently:

```
Microsoft.Orleans.Core 9.1.2-granville
Microsoft.Orleans.Runtime 9.1.2-granville  
Microsoft.Orleans.Serialization 9.1.2-granville
```

These packages would contain your InternalsVisibleTo modifications.

**Pros:**
- No type forwarding needed
- UFX SignalR and other packages "just work"
- Clear versioning shows it's modified
- Easy to switch back to official packages later

**Cons:**
- Could be confusing (though the -granville suffix helps)

## Multi-Silo Considerations

For 2-3 silo demonstration with UFX SignalR:

1. **SignalR Backplane**: Requires grain observer patterns for real-time updates across silos
2. **Zone Distribution**: ActionServers need to coordinate zone ownership across silos
3. **Membership**: Orleans cluster membership ensures ActionServers know about silo changes

## Recommendation

Given the goals of demonstrating RPC coexistence with Orleans while supporting advanced features like UFX SignalR, **Option 4 (Strategic Package Naming)** provides the best balance of:

1. **Simplicity**: No type forwarding maintenance
2. **Compatibility**: UFX SignalR and other packages work without modification
3. **Clear Intent**: The `-granville` suffix shows these are modified packages
4. **Easy Migration**: When Orleans adds InternalsVisibleTo officially, just switch package versions
5. **Demo Ready**: Multi-silo Shooter demo works immediately

The package structure would be:
```
local-packages/
  Microsoft.Orleans.Core.9.1.2-granville.nupkg (with InternalsVisibleTo)
  Microsoft.Orleans.Runtime.9.1.2-granville.nupkg (with InternalsVisibleTo)
  Microsoft.Orleans.Serialization.9.1.2-granville.nupkg (with InternalsVisibleTo)
  Granville.Rpc.*.9.1.2.nupkg (your RPC packages)
```

This approach enables full functionality while clearly demonstrating the minimal changes needed for official Orleans support.