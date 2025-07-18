# Granville RPC Serialization Strategy

## Overview

This document outlines the comprehensive serialization strategy for Granville RPC, addressing fundamental type serialization issues and aligning with Orleans' proven patterns.

## Background

### The VoidTaskResult Problem

We discovered a critical issue where methods returning `Task` (without a result type) were causing serialization failures:

```
Orleans.Serialization.CodecNotFoundException: Could not find a codec for type System.Threading.Tasks.VoidTaskResult
```

This occurs because:
1. When a method returns `Task` (not `Task<T>`), accessing `task.Result` returns a `VoidTaskResult` object
2. `VoidTaskResult` is an internal .NET type with no registered Orleans serializer
3. Our RPC layer was attempting to serialize this internal type

### Orleans' Approach

Orleans solves this elegantly:
- Uses `CompletedResponse` (a singleton) for void method returns
- Never attempts to serialize `VoidTaskResult`
- Registers `CompletedResponse` as well-known type ID 36
- Uses `Response<T>` wrapper for methods with return values

## Strategic Decision

After analysis, we've chosen a **Hybrid Strategy** that:
1. Provides immediate fixes for current issues
2. Plans migration to Orleans' proven patterns
3. Ensures comprehensive type coverage

## Implementation Phases

### Phase 1: Immediate Fix

1. **Debug Current Fix**
   - Verify Task vs Task<T> detection logic execution
   - Add detailed serialization path logging
   - Identify all serialization points

2. **Safety Net Serializers**
   - Create VoidTaskResult serializer (serializes to empty/null)
   - Register as Granville RPC well-known type
   - Add other fundamental type serializers

### Phase 2: Medium-term Solution

3. **Align Response Handling**
   - Introduce `Response<T>` and `CompletedResponse` in Granville.Rpc.Abstractions
   - Modify RpcConnection to use CompletedResponse for void methods
   - Update serialization for proper Response type handling

4. **Comprehensive Type Registration**
   - Create GranvilleRpcWellKnownTypes registry
   - Register all RPC fundamental types
   - Include primitives, tasks, collections, common .NET types

### Phase 3: Long-term Architecture

5. **Full Orleans Alignment**
   - Adopt Orleans' invocation pipeline benefits
   - Use Orleans' response completion patterns
   - Leverage Orleans' type manifest system fully

## Technical Details

### Current Architecture
```
RpcConnection.ProcessRequestAsync()
  └─> InvokeGrainMethodAsync()
      └─> method.Invoke() returns Task
      └─> Extracts task.Result (VoidTaskResult)
      └─> SerializeResult() fails - no codec
```

### Target Architecture
```
RpcConnection.ProcessRequestAsync()
  └─> InvokeGrainMethodAsync()
      └─> method.Invoke() returns Task
      └─> Detects void return
      └─> Returns CompletedResponse.Instance
      └─> SerializeResult() succeeds - well-known type
```

## Implementation Priority

1. **Critical**: Fix VoidTaskResult serialization blocking bots
2. **High**: Add comprehensive fundamental type support
3. **Medium**: Migrate to Orleans response patterns
4. **Low**: Full architectural alignment

## Success Criteria

- No serialization errors in Shooter sample logs
- All fundamental .NET types properly serialized
- Clear path to Orleans pattern alignment
- Maintainable and extensible type system