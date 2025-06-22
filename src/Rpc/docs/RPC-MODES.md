# RPC Modes and Service Registration Strategy

## Overview

Orleans RPC can operate in three different modes, each with different service registration requirements. This document explains these modes and why certain services need both keyed and unkeyed registrations.

## The Three RPC Modes

### 1. Standalone RPC Client Mode
In this mode, an application uses ONLY the RPC client without any Orleans components.

**Example:**
```csharp
var builder = WebApplication.CreateBuilder(args);
builder.UseRpcClient()  // Only RPC client, no Orleans
    .ConfigureRpcClient(options => {
        options.ServerAddress = "localhost:5000";
    });
```

**Characteristics:**
- No Orleans client or silo present
- RPC is the only grain communication mechanism
- All grain-related services come from RPC

### 2. Standalone RPC Server Mode
In this mode, an application runs ONLY an RPC server without any Orleans components.

**Example:**
```csharp
var builder = WebApplication.CreateBuilder(args);
builder.UseRpcServer()  // Only RPC server, no Orleans
    .ConfigureRpcServer(options => {
        options.ListenAddress = "localhost:5000";
    });
```

**Characteristics:**
- No Orleans silo present
- RPC handles all grain hosting and activation
- All grain-related services come from RPC

### 3. Triple Mode (Orleans + RPC)
In this mode, an application uses BOTH Orleans (client or silo) AND RPC (client or server).

**Example - ActionServer:**
```csharp
var builder = WebApplication.CreateBuilder(args);
builder.UseOrleans(siloBuilder => {
    // Configure Orleans silo
})
.UseRpcServer(rpcBuilder => {
    // Configure RPC server
});
```

**Characteristics:**
- Both Orleans and RPC are present
- Services can conflict if not properly isolated
- Need to ensure RPC uses its own services, not Orleans'

## The Service Registration Problem

When both Orleans and RPC are present (triple mode), they both register similar services:
- `IClusterManifestProvider` - provides grain metadata
- `GrainPropertiesResolver` - resolves grain properties
- `GrainInterfaceTypeToGrainTypeResolver` - maps interfaces to grain types
- `GrainFactory` - creates grain references

Without proper isolation, RPC might use Orleans' services, causing incorrect behavior.

## The Keyed Service Solution

To solve this, we use .NET's keyed services feature:

```csharp
// Register RPC's version as keyed service
services.AddKeyedSingleton<GrainPropertiesResolver>("rpc", (sp, key) => 
    new GrainPropertiesResolver(
        sp.GetRequiredKeyedService<IClusterManifestProvider>("rpc")
    ));
```

This ensures:
1. RPC components can request RPC-specific services using the "rpc" key
2. Orleans components continue to use unkeyed services
3. No conflicts between the two systems

## Why Also Register as Unkeyed?

In standalone mode (RPC only, no Orleans), existing code expects to find services without a key:

```csharp
// This code expects an unkeyed GrainPropertiesResolver
var resolver = serviceProvider.GetRequiredService<GrainPropertiesResolver>();
```

To support this, we also register as unkeyed, but use `TryAddSingleton`:

```csharp
// For standalone mode, also register as unkeyed
services.TryAddSingleton<GrainPropertiesResolver>(sp => 
    sp.GetRequiredKeyedService<GrainPropertiesResolver>("rpc"));
```

The `TryAddSingleton` ensures:
- In standalone mode: RPC's service is registered as unkeyed
- In triple mode: Orleans' unkeyed service takes precedence, RPC uses keyed

## Implementation Pattern

For each service that could conflict:

1. **Register keyed version for RPC:**
   ```csharp
   services.AddKeyedSingleton<ServiceType>("rpc", factory);
   ```

2. **Register unkeyed for standalone mode:**
   ```csharp
   services.TryAddSingleton<ServiceType>(sp => 
       sp.GetRequiredKeyedService<ServiceType>("rpc"));
   ```

3. **Update consumers to use keyed service:**
   ```csharp
   // In RPC components
   sp.GetRequiredKeyedService<ServiceType>("rpc")
   ```

## Services Currently Using This Pattern

- `IClusterManifestProvider` - ✅ Implemented
- `GrainInterfaceTypeToGrainTypeResolver` - ✅ Implemented
- `GrainFactory` / `IGrainFactory` - ✅ Implemented
- `GrainPropertiesResolver` - ✅ Implemented (this PR)
- `GrainClassMap` - ✅ Implemented

## Benefits

1. **Isolation**: RPC and Orleans services don't interfere with each other
2. **Compatibility**: Standalone mode works without code changes
3. **Flexibility**: Applications can use Orleans, RPC, or both
4. **Predictability**: Each system uses its own services consistently