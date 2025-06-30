# Orleans Coupling Fragility Analysis

*Generated: June 29, 2025*

## Overview

This document analyzes the coupling between Granville RPC and Orleans internals, and what would happen if Forkleans Orleans DLLs were replaced with official Microsoft Orleans DLLs.

**Important Context**: Forkleans is forked from the Orleans main branch (unreleased version 9.2.0), NOT from the released Orleans 9.1.2. This creates a situation where:
- Forkleans is slightly ahead of the latest official Orleans release
- Once Orleans 9.2.0 is officially released, version compatibility will improve

## Current InternalsVisibleTo Grants

The RPC assemblies have been granted internal access to Orleans assemblies through InternalsVisibleTo attributes:

### Orleans.Core grants internal access to:
1. Granville.Rpc.Server
2. Granville.Rpc.Client
3. Granville.Rpc.Transport.LiteNetLib
4. Granville.Rpc.Transport.Ruffles
5. Granville.Rpc.Tests
6. Granville.Rpc.Server.Tests
7. Granville.Rpc.Client.Tests

### Orleans.Runtime grants internal access to:
1. Granville.Rpc.Server
2. Granville.Rpc.Client
3. Granville.Rpc.Transport.LiteNetLib
4. Granville.Rpc.Transport.Ruffles
5. Granville.Rpc.Tests
6. Granville.Rpc.Server.Tests
7. Granville.Rpc.Client.Tests

## What Would Break If Using Official Orleans DLLs

### 1. Loss of Internal Access
- **Compilation Errors**: Any RPC code that references Orleans internal types, methods, or properties would fail to compile
- **Deep Integration Features**: RPC's integration with Orleans networking, serialization, and runtime would likely break
- **Transport Layer**: LiteNetLib and Ruffles transports may depend on Orleans internals for packet handling

### 2. Version Compatibility Issues
The version gap is relatively small:
- Official Orleans is at 9.1.2 (released)
- Forkleans is at 9.2.0.43-preview3 (based on unreleased main)
- Once Orleans 9.2.0 is released, versions will align
- However, any API changes between the fork point and official release could cause issues

### 3. Namespace Conflicts
The primary ongoing issue:
- Forkleans uses `Granville.*` namespaces
- Official Orleans uses `Orleans.*` namespaces
- This prevents using any Orleans ecosystem packages without modification

## What Would Still Work

### 1. Public API Usage
- Any RPC code that only uses Orleans public APIs would continue to function
- Basic grain interactions, storage providers, and standard features would work

### 2. Third-Party Packages (with caveats)
Once namespace issues are resolved:
- UFX.Orleans.SignalRBackplane would work
- OrgnalR and other Orleans packages would be compatible
- No assembly binding redirect complexity

Currently blocked by:
- Namespace differences (Granville vs Orleans)

## Implications

### Current State
1. **Strong Coupling**: RPC has deep dependencies on Orleans internals
2. **Ecosystem Isolation**: Cannot use Orleans ecosystem packages without significant work
3. **Maintenance Burden**: Must maintain fork and update InternalsVisibleTo with each Orleans update

### Future Considerations
1. **When Orleans 9.2.0 releases**: Version gap closes, but namespace issues remain
2. **Namespace Strategy**: Plan to revert Orleans namespaces while keeping RPC as Granville would solve ecosystem compatibility
3. **Internal Dependency Audit**: Should identify exactly which internals RPC uses and consider alternatives

## Recommendations

### Short Term
1. Document all internal Orleans types/members used by RPC
2. Evaluate if any internal usage can be replaced with public APIs
3. Continue with current approach until Orleans 9.2.0 release

### Long Term
1. Implement namespace strategy (Orleans for core, Granville for RPC)
2. Consider proposing some internals as public APIs to Orleans project
3. Maintain minimal internal coupling for easier maintenance

### Risk Mitigation
1. Create integration tests that verify RPC works with Orleans internals
2. Monitor Orleans main branch for changes that might affect RPC
3. Plan for gradual reduction of internal dependencies

## Conclusion

The coupling between RPC and Orleans internals is significant but manageable. The main challenges are:
1. **Namespace differences** preventing ecosystem compatibility
2. **Internal access requirements** that tie RPC to specific Orleans versions

The planned namespace strategy (reverting to Orleans.* for core functionality) would solve most ecosystem compatibility issues while maintaining the Granville brand for RPC innovations.