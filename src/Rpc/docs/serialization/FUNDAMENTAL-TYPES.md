# Fundamental Types for Granville RPC Serialization

## Overview

This document lists all fundamental types that require serialization support in Granville RPC.

## Core .NET Types

### Task Types
- `Task` - Methods with no return value
- `Task<T>` - Methods with return value
- `ValueTask` - Struct-based async with no return
- `ValueTask<T>` - Struct-based async with return
- `VoidTaskResult` - Internal type from Task.Result
- `IAsyncEnumerable<T>` - Async streaming

### Primitive Types
Already handled by Orleans, but listed for completeness:
- `bool`, `byte`, `sbyte`
- `short`, `ushort`, `int`, `uint`, `long`, `ulong`
- `float`, `double`, `decimal`
- `char`, `string`
- `DateTime`, `DateTimeOffset`, `TimeSpan`
- `Guid`
- `byte[]`

### Collection Types
- `Array` (all dimensions)
- `List<T>`, `IList<T>`
- `Dictionary<TKey, TValue>`, `IDictionary<TKey, TValue>`
- `HashSet<T>`, `ISet<T>`
- `Queue<T>`, `Stack<T>`
- `IEnumerable<T>`, `ICollection<T>`
- `ReadOnlyCollection<T>`, `IReadOnlyList<T>`, `IReadOnlyDictionary<TKey, TValue>`

### Nullable Types
- `Nullable<T>` (T?)
- `string?` (already nullable)
- Reference type nullability annotations

### Exception Types
- `Exception` (base)
- `ArgumentException`, `ArgumentNullException`
- `InvalidOperationException`
- `TimeoutException`
- `TaskCanceledException`
- `OperationCanceledException`
- Custom RPC exceptions

### Cancellation Types
- `CancellationToken`
- `CancellationTokenSource`

### Tuple Types
- `ValueTuple<T1>` through `ValueTuple<T1,T2,T3,T4,T5,T6,T7,TRest>`
- `Tuple<T1>` through `Tuple<T1,T2,T3,T4,T5,T6,T7,TRest>`
- Named tuples

### Memory Types
- `Memory<T>`
- `ReadOnlyMemory<T>`
- `Span<T>` (limited serialization)
- `ReadOnlySpan<T>` (limited serialization)
- `ArraySegment<T>`

## RPC-Specific Types

### Orleans Integration Types
- `Response<T>` - Orleans response wrapper
- `CompletedResponse` - Orleans void response
- `GrainReference` - Grain references
- `GrainId` - Grain identifiers

### Protocol Types (from Granville.Rpc.Abstractions)
- `RpcMessage` (base)
- `RpcHandshake`
- `RpcHandshakeAck`
- `RpcRequest`
- `RpcResponse`
- `RpcAsyncEnumerableRequest`
- `RpcAsyncEnumerableItem`
- `RpcAsyncEnumerableComplete`
- `RpcAsyncEnumerableCancel`
- `RpcAsyncEnumerableError`
- `RpcUnsubscribe`
- `RpcHeartbeat`

### Delivery Types
- `RpcDeliveryMode` enum
- `RpcPriority` enum

## Registration Strategy

### Well-Known Types
Types registered with fixed IDs for efficient serialization:
1. CompletedResponse (ID: custom)
2. VoidTaskResult (ID: custom)
3. Common exceptions (IDs: custom range)

### Dynamic Types
Types discovered and registered at runtime:
- User-defined types with [GenerateSerializer]
- Generic type instantiations
- Dynamic assemblies

## Implementation Priority

1. **Critical** (Blocking Issues):
   - VoidTaskResult
   - Task/ValueTask handling
   - Basic protocol types

2. **High** (Common Usage):
   - Collection types
   - Nullable handling
   - Common exceptions

3. **Medium** (Full Compatibility):
   - Tuple types
   - Memory types
   - Cancellation tokens

4. **Low** (Edge Cases):
   - Span types
   - Custom serialization
   - Performance optimizations