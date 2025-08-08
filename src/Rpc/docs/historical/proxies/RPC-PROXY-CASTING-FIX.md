# RPC Proxy Casting Fix (v128)

## Problem
RPC grain references couldn't be cast to their interfaces, causing `InvalidCastException`.

## Solution
Implemented dynamic proxy wrappers using Reflection.Emit as a temporary fix.

## Documentation
See `/granville/subprojects/RpcProxies/README.md` for:
- Complete problem analysis
- What we tried and why it failed
- Current implementation details
- Future compile-time generation plans

## Key Files
- `RpcInterfaceProxyFactory.cs` - Dynamic proxy generation
- `RpcGrainReferenceActivatorProvider.cs` - Proxy activation