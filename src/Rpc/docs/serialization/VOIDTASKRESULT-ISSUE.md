# VoidTaskResult Serialization Issue

## Problem Description

When RPC methods return `Task` (not `Task<T>`), the serialization layer encounters:
```
Orleans.Serialization.CodecNotFoundException: Could not find a codec for type System.Threading.Tasks.VoidTaskResult
```

## Root Cause Analysis

### What is VoidTaskResult?

`VoidTaskResult` is an internal .NET type used as a placeholder when accessing the `Result` property of a non-generic `Task`:

```csharp
// Method signature
Task UpdatePlayerInputEx(string playerId, Vector2? moveDirection, Vector2? shootDirection);

// When executed
Task task = UpdatePlayerInputEx(...);
await task;
var result = task.Result;  // Returns VoidTaskResult instance
```

### Why It Fails

1. Orleans serialization requires explicit codecs for all types
2. `VoidTaskResult` is internal to .NET and has no registered codec
3. Our RPC layer was attempting to serialize this internal type

## Affected Methods in Shooter Sample

All methods returning `Task` without a result type:
- `DisconnectPlayer(string playerId)`
- `UpdatePlayerInput(string playerId, Vector2 moveDirection)`
- `UpdatePlayerInputEx(string playerId, Vector2? moveDirection, Vector2? shootDirection)`
- `ReceiveScoutAlert(string playerId, ScoutAlert alert)`
- `TransferBulletTrajectory(BulletTransfer transfer)`
- `Subscribe(IGameObserver observer)`
- `Unsubscribe(IGameObserver observer)`
- `SendChatMessage(string playerId, string message)`
- `NotifyBulletDestroyed(Guid bulletId)`

## Error Stack Trace

```
at Orleans.Serialization.Serializers.CodecProvider.ThrowCodecNotFound(Type fieldType)
at Orleans.Serialization.Serializers.CodecProvider.GetCodec(Type fieldType)
at Orleans.Serialization.Codecs.ObjectCodec.WriteField[TBufferWriter](...)
at Orleans.Serialization.Serializer.Serialize[T,TBufferWriter](...)
at Granville.Rpc.RpcConnection.SerializeResult(Object result)
at Granville.Rpc.RpcConnection.ProcessRequestAsync(RpcRequest request)
```

## Impact

- Affects all bot clients calling void methods
- Causes frequent errors in ActionServer logs
- Creates connection instability for automated testing
- Does not affect human clients (different call patterns)

## Solution Approaches

### Attempted Fix
Modified `RpcConnection.InvokeGrainMethodAsync()` to detect non-generic Tasks and return null:
```csharp
if (taskType == typeof(Task))
{
    return null;
}
```

### Why It May Still Fail
1. Code might not be deployed/recompiled correctly
2. Multiple serialization paths exist
3. Race conditions in async processing
4. Other code paths accessing Task.Result

### Recommended Solutions

1. **Immediate**: Add VoidTaskResult serializer
2. **Better**: Use Orleans' CompletedResponse pattern
3. **Best**: Align with Orleans invocation model

## Related Files

- `/src/Rpc/Orleans.Rpc.Server/RpcConnection.cs` - Where serialization happens
- `/src/Orleans.Serialization/Invocation/Response.cs` - Orleans' approach
- `/src/Orleans.Serialization/Configuration/DefaultTypeManifestProvider.cs` - Well-known types