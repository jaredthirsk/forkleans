# RPC Client Assembly Registration Issue

## Problem
The Shooter sample was experiencing null player ID errors because string arguments were not being properly serialized in RPC calls. The root cause was that the RPC client was only registering the RPC protocol assemblies for serialization, but not the application assemblies containing the actual data models.

## Root Cause Analysis
When examining the RPC client configuration in `GranvilleRpcGameClientService.cs`, we found:

```csharp
services.AddSerializer(serializer =>
{
    serializer.AddAssembly(typeof(IGameRpcGrain).Assembly);  // Grain interfaces
    serializer.AddAssembly(typeof(Granville.Rpc.Protocol.RpcMessage).Assembly);  // RPC protocol
    // Missing: serializer.AddAssembly(typeof(Player).Assembly);  // Application models!
});
```

The client was missing registration of the `Shooter.Shared` assembly, which contains:
- `Player` class
- `WorldState` class
- Other game models and DTOs

Without this assembly registered, the Orleans serializer couldn't properly serialize/deserialize these types, resulting in null values being passed through RPC.

## Solution
Add the application assembly to the serialization configuration:

```csharp
services.AddSerializer(serializer =>
{
    serializer.AddAssembly(typeof(IGameRpcGrain).Assembly);
    serializer.AddAssembly(typeof(Granville.Rpc.Protocol.RpcMessage).Assembly);
    // Add application models assembly
    serializer.AddAssembly(typeof(Player).Assembly);  // This is Shooter.Shared
});
```

## Key Learnings

1. **Both client and server must register the same assemblies** - The server was correctly registering `Shooter.Shared`, but the client wasn't.

2. **Orleans serialization requires explicit assembly registration** - Unlike some serializers that can discover types at runtime, Orleans requires upfront registration of all assemblies containing serializable types.

3. **Missing assemblies cause silent failures** - When an assembly isn't registered, Orleans may serialize values as null or use fallback serialization that doesn't work correctly.

## Recommendations for RPC Applications

1. **Create a shared serialization configuration method**:
   ```csharp
   public static void ConfigureSharedSerialization(ISerializerBuilder serializer)
   {
       // RPC protocol
       serializer.AddAssembly(typeof(RpcMessage).Assembly);
       
       // Application assemblies
       serializer.AddAssembly(typeof(YourSharedModels).Assembly);
       serializer.AddAssembly(typeof(YourGrainInterfaces).Assembly);
   }
   ```

2. **Use this configuration in both client and server**:
   ```csharp
   // In server
   services.AddSerializer(ConfigureSharedSerialization);
   
   // In client  
   services.AddSerializer(ConfigureSharedSerialization);
   ```

3. **Document required assemblies** - Maintain a list of assemblies that must be registered for serialization.

4. **Consider automatic assembly discovery** - For larger applications, consider implementing automatic discovery of assemblies marked with a custom attribute.

## Testing
After adding the missing assembly registration, the "null player ID" errors should be resolved, and the Shooter sample should work correctly with proper serialization of all game models.