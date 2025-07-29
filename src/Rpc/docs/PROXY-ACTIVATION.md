# RPC Proxy Activation and Registration

## Table of Contents
1. [Overview](#overview)
2. [Recent Fix: Interface Mapping Uniqueness (v9.1.2.155)](#recent-fix-interface-mapping-uniqueness-v912155)
3. [Current Connection Issues (v9.1.2.158)](#current-connection-issues-v912158)
4. [How RPC Proxy Activation Works](#how-rpc-proxy-activation-works)
5. [Troubleshooting Guide](#troubleshooting-guide)
6. [Future Improvements](#future-improvements)

## Overview

This document explains how RPC proxy activation works and recent fixes to the manifest registration system that ensure proper interface-to-grain mappings when multiple RPC servers connect.

## Recent Fix: Interface Mapping Uniqueness (v9.1.2.155)

### Problem
When multiple RPC servers send their manifests to the client, the `MultiServerManifestProvider` was not properly handling duplicate interface mappings. This caused issues in two areas:

1. **Duplicate Key Collisions**: When merging manifests from multiple servers, the same grain types and interface types could be added multiple times, causing key collision errors.

2. **Interface Property Format**: The interface-to-grain mappings were being stored with incorrect property keys like `"interface.Shooter.Grains.IPlayerGrain"` instead of the Orleans-expected format `"interface.0"`, `"interface.1"`, etc.

### Root Cause Analysis

The Orleans `GrainInterfaceTypeToGrainTypeResolver` expects grain properties to follow a specific convention:
- Keys: `"{WellKnownGrainTypeProperties.ImplementedInterfacePrefix}{index}"` (e.g., `"interface.0"`, `"interface.1"`)
- Values: Interface type ID (e.g., `"Shooter.Grains.IPlayerGrain"`)

The bug was creating keys with the interface type embedded in the key itself, which Orleans couldn't parse.

### Solution

Two key changes were made to `MultiServerManifestProvider`:

#### 1. Preventing Duplicate Registrations

```csharp
// In RebuildCompositeManifest()
foreach (var kvp in silo.Grains)
{
    // Only add if not already present to avoid duplicate interface mappings
    if (!grainsBuilder.ContainsKey(kvp.Key))
    {
        grainsBuilder[kvp.Key] = kvp.Value;
    }
}
```

#### 2. Correct Interface Property Format

```csharp
// In BuildClusterManifest()
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

// Add each interface with a numeric index
foreach (var grainGroup in grainInterfaces)
{
    var grainType = GrainType.Create(grainGroup.Key);
    var counter = 0;
    
    // Determine starting counter by checking existing properties
    while (props.ContainsKey($"{WellKnownGrainTypeProperties.ImplementedInterfacePrefix}{counter}"))
    {
        counter++;
    }
    
    // Add interfaces with proper numeric indices
    foreach (var interfaceTypeStr in grainGroup.Value)
    {
        var interfaceType = GrainInterfaceType.Create(interfaceTypeStr);
        var key = $"{WellKnownGrainTypeProperties.ImplementedInterfacePrefix}{counter}";
        if (!props.ContainsKey(key))
        {
            props = props.Add(key, interfaceType.ToString());
        }
        counter++;
    }
}
```

## How RPC Proxy Activation Works

### 1. Server Manifest Registration

When an RPC server starts:
1. It creates a manifest containing all available grain types and their interfaces
2. During handshake with clients, it sends this manifest via `RpcGrainManifest`
3. The manifest includes:
   - `GrainProperties`: Metadata about each grain type
   - `InterfaceProperties`: Metadata about each interface type
   - `InterfaceToGrainMappings`: Maps interface types to their implementing grain types

### 2. Client Manifest Processing

When a client receives a server manifest:
1. `MultiServerManifestProvider.UpdateFromServerAsync()` is called
2. The server's manifest is stored separately by server ID
3. A composite manifest is rebuilt by merging all server manifests
4. Duplicate grain/interface types are skipped to prevent collisions

### 3. Proxy Creation

When `GetGrain<T>()` is called:
1. `RpcGrainFactory` creates an `RpcGrainReference`
2. `RpcGrainReferenceActivatorProvider` uses the composite manifest to resolve the interface type
3. Orleans' `GrainInterfaceTypeToGrainTypeResolver` looks up the grain type using the interface properties
4. A dynamic proxy is created that routes calls through the RPC transport

### 4. Multiple Server Scenarios

The system handles multiple RPC servers by:
1. Maintaining separate manifests per server
2. Building a unified view in the composite manifest
3. Using "first-wins" strategy for duplicate types
4. Properly formatting interface properties for Orleans compatibility

## Key Components

### MultiServerManifestProvider
- Maintains separate manifests for each connected server
- Builds composite manifest with proper deduplication
- Formats interface properties in Orleans-compatible way

### RpcGrainFactory
- Creates grain references using the composite manifest
- Routes calls to appropriate RPC servers

### RpcGrainReference
- Implements `IGrainReference` for Orleans compatibility
- Routes method calls through RPC transport
- Handles server selection and failover

## Testing

The fix can be verified by:
1. Starting multiple RPC servers with overlapping grain types
2. Connecting a client to all servers
3. Calling `GetGrain<T>()` for shared interfaces
4. Verifying no timeout or key collision errors occur

## Current Connection Issues (v9.1.2.158)

### Symptoms
Despite the manifest fix in v9.1.2.155, clients are still experiencing connection issues:

1. **GetGrain Timeout**: Client calls to `GetGrain<T>()` timeout after 2 seconds
2. **Connection Failures**: Some clients fail to establish initial UDP connection via LiteNetLib
3. **Manifest Population**: Even when connected, the manifest may not be properly populated

### Analysis

From log analysis and testing:

1. **Server Side**: ActionServers are successfully:
   - Starting RPC server on port 12000 (and dynamic ports)
   - Accepting connections from clients
   - Sending handshake with manifest containing 10 grains and 48 interfaces
   - Processing handshakes successfully

2. **Client Side Issues**:
   - Some clients timeout during initial UDP connection establishment
   - Successfully connected clients still timeout on `GetGrain` calls
   - The keyed `IGrainFactory` service may not be properly registered

### Root Causes

1. **Network/Firewall Issues**: 
   - UDP packets may be blocked
   - Windows Firewall or WSL networking issues
   - Port conflicts or binding issues

2. **Service Registration**:
   - The RPC grain factory may not be properly registered as a keyed service
   - Circular dependencies during service initialization

3. **Timing Issues**:
   - Manifest may not be fully populated before `GetGrain` is called
   - Race condition between connection establishment and manifest exchange

## Troubleshooting Guide

### 1. Verify Server is Listening

Check ActionServer logs for RPC port:
```bash
grep "RPC port\|listening on" logs/actionserver-*.log
```

Expected output:
```
RPC server  is listening on 0.0.0.0:12000
Registering ActionServer ... RPC port: 12000
```

### 2. Test Basic Connectivity

Use the test program in `/granville/samples/Rpc/test-rpc-connection.ps1`:
```powershell
pwsh ./test-rpc-connection.ps1
dotnet-win run --project TestRpcConnection/TestRpcConnection.csproj
```

### 3. Check Manifest Population

In client logs, look for:
- "Manifest provider type" - Should show `MultiServerManifestProvider`
- "Current manifest version" - Should be non-zero
- "Grain count" / "Interface count" - Should show actual counts

### 4. Debug GetGrain Flow

1. Enable debug logging:
```csharp
logging.AddFilter("Granville.Rpc", LogLevel.Debug);
logging.AddFilter("Orleans", LogLevel.Debug);
```

2. Check for these log entries:
- `RpcClient.GetGrain<{Interface}> called`
- `EnsureConnected called, connection count = {count}`
- `Getting keyed IGrainFactory service with key 'rpc'`

### 5. Common Fixes

1. **Firewall**: Allow UDP traffic on RPC ports (12000-13000)
2. **WSL**: Use `127.0.0.1` instead of `localhost`
3. **Timing**: Add delay after connection before calling GetGrain
4. **Service Registration**: Ensure RPC services are properly registered

## Key Components in Detail

### RpcClient Service Registration

The RPC client must register several keyed services:
```csharp
services.AddKeyedSingleton<IGrainFactory, RpcGrainFactory>("rpc");
services.AddKeyedSingleton<IClusterManifestProvider, MultiServerManifestProvider>("rpc");
```

### Connection Flow

1. `RpcClient.StartAsync()` → `ConnectToInitialServersAsync()`
2. `LiteNetLibTransport.ConnectAsync()` with 5s timeout
3. On connection: Send `RpcHandshake` message
4. Server responds with `RpcHandshakeAcknowledgment` + manifest
5. `MultiServerManifestProvider.UpdateFromServerAsync()` processes manifest
6. `GetGrain` can now resolve interfaces to grain types

### Manifest Exchange Details

Server sends:
```
RpcHandshakeAcknowledgment {
  ProtocolVersion: 1,
  ServerId: "server-{id}",
  GrainManifest: {
    GrainProperties: { /* 10 grains */ },
    InterfaceProperties: { /* 48 interfaces */ },
    InterfaceToGrainMappings: { /* mappings */ }
  }
}
```

Client processes and builds composite manifest with proper interface indexing.

## Implemented Fix: WaitForManifestAsync (v9.1.2.159)

### Problem
The core issue was a service registration timing race condition. The `StartAsync()` method would return before the manifest was populated from connected servers, causing `GetGrain` calls to fail immediately after startup.

### Solution: WaitForManifestAsync Method

A new method has been added to the `IRpcClient` interface to allow clients to wait for the manifest to be populated:

```csharp
public interface IRpcClient : IClusterClient
{
    /// <summary>
    /// Waits for the manifest to be populated from at least one server.
    /// </summary>
    /// <param name="timeout">The maximum time to wait. Default is 10 seconds.</param>
    /// <returns>A task that completes when the manifest is ready.</returns>
    /// <exception cref="TimeoutException">Thrown if the manifest is not populated within the timeout.</exception>
    Task WaitForManifestAsync(TimeSpan timeout = default);
}
```

### Usage

Clients should now call `WaitForManifestAsync` after starting the host but before calling `GetGrain`:

```csharp
// Start the host
await host.StartAsync();

// Get the RPC client
var rpcClient = host.Services.GetRequiredService<IRpcClient>();

// Wait for manifest to be ready
await rpcClient.WaitForManifestAsync(TimeSpan.FromSeconds(10));

// Now safe to get grains
var grain = rpcClient.GetGrain<IGameRpcGrain>("game");
```

### Implementation Details

The `WaitForManifestAsync` method:
1. Checks if the manifest provider is `MultiServerManifestProvider`
2. Polls the manifest every 50ms until grains are present
3. Times out after the specified duration (default 10 seconds)
4. Logs progress and final state for debugging

### Benefits

1. **Eliminates race conditions**: Ensures manifest is ready before grain resolution
2. **Clear error messages**: Throws `TimeoutException` with helpful message
3. **Configurable timeout**: Allows adjustment based on network conditions
4. **Non-breaking change**: Existing code continues to work (though may timeout)

### Testing Results

The fix has been tested with version 9.1.2.159:
1. Clients can now successfully connect to RPC servers
2. The `WaitForManifestAsync` method properly waits for manifest population
3. `GetGrain` calls succeed after manifest is ready
4. No more 2-second timeouts on initial grain acquisition

## Summary

The RPC proxy activation system has evolved through several fixes:

1. **v9.1.2.155**: Fixed interface mapping format and duplicate key collisions in manifest merging
2. **v9.1.2.159**: Added `WaitForManifestAsync` to resolve timing race conditions

These fixes ensure reliable RPC proxy activation in multi-server scenarios with proper interface resolution and timing synchronization.

## Future Improvements

1. **Automatic Waiting**: Make StartAsync wait for initial manifest automatically
2. **Connection State Machine**: Implement proper connection states for better visibility
3. **Retry Logic**: Add exponential backoff for connection attempts
4. **Service Discovery**: Support dynamic server discovery
5. **Connection Pooling**: Reuse connections across multiple RPC clients
6. **Health Checks**: Add connection health monitoring