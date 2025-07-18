# Orleans Binary Serialization Client Fix

## Issue
After switching from JSON to Orleans binary serialization, ActionServer logs were showing serialization errors. The issue was that the RPC protocol message types (RpcRequest, RpcResponse, etc.) marked with `[GenerateSerializer]` were not being registered with the Orleans serializer in client applications.

## Root Cause
While the RPC server framework (in `DefaultRpcServerServices.cs`) properly registers the RPC protocol assembly:
```csharp
services.AddSerializer(serializer =>
{
    // Add the RPC abstractions assembly for protocol messages
    serializer.AddAssembly(typeof(Protocol.RpcMessage).Assembly);
});
```

Client applications creating RPC connections were only registering their grain interface assemblies, missing the RPC protocol assembly.

## Solution
Added RPC protocol assembly registration to all RPC client configurations:

### 1. Shooter.ActionServer/Program.cs
```csharp
builder.Services.AddSerializer(serializerBuilder =>
{
    serializerBuilder.AddAssembly(typeof(Shooter.Shared.GrainInterfaces.IWorldManagerGrain).Assembly);
    // Add RPC protocol assembly for RPC message serialization
    serializerBuilder.AddAssembly(typeof(Granville.Rpc.Protocol.RpcMessage).Assembly);
});
```

### 2. Shooter.ActionServer/Services/CrossZoneRpcService.cs
```csharp
.ConfigureServices(services =>
{
    services.AddSerializer(serializer =>
    {
        serializer.AddAssembly(typeof(IGameRpcGrain).Assembly);
        // Add RPC protocol assembly for RPC message serialization
        serializer.AddAssembly(typeof(Granville.Rpc.Protocol.RpcMessage).Assembly);
    });
})
```

### 3. Shooter.ActionServer/Services/GameService.cs
```csharp
.ConfigureServices(services =>
{
    services.AddSerializer(serializer =>
    {
        serializer.AddAssembly(typeof(IGameRpcGrain).Assembly);
        // Add RPC protocol assembly for RPC message serialization
        serializer.AddAssembly(typeof(Granville.Rpc.Protocol.RpcMessage).Assembly);
    });
})
```

### 4. Shooter.Client.Common/GranvilleRpcGameClientService.cs
Updated all 4 occurrences of RPC client creation to include the protocol assembly.

## Recommendation for Framework Improvement
Consider updating the RPC client builder extensions to automatically register the RPC protocol assembly, similar to how the server does it. This would prevent users from needing to manually register it.

## Testing
After these changes, the ActionServer should no longer show serialization errors when using Orleans binary serialization for RPC messages.

---

*Fix implemented: July 17, 2025*