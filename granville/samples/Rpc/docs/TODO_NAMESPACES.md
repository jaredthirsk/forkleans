# TODO: Namespace Strategy for Orleans Compatibility

## Implementation Status (2025-06-30) - COMPLETED

**Major Achievement**: Successfully implemented the hybrid namespace strategy:
- **Orleans namespaces**: Reverted back to Orleans.* for all non-RPC projects (1776 files)
- **RPC namespaces**: Kept as Granville.Rpc.* for clear separation
- **UFX.Orleans.SignalRBackplane**: Re-enabled in Shooter.Silo
- **InternalsVisibleTo**: Updated to reference Granville.Rpc assemblies

This allows maximum compatibility with third-party Orleans packages while maintaining clear product differentiation for the RPC innovation.

## Current Status (2025-06-30)

### What We Have Now (After Implementation)
- **Orleans Namespaces**: Reverted back to `Orleans.*` for all non-RPC code
- **RPC Namespaces**: Using `Granville.Rpc.*` namespace hierarchy for clear separation
- **DLL Names**: Orleans DLLs back to `Orleans.*` (e.g., `Orleans.Core.dll`, `Orleans.Runtime.dll`)
- **Package IDs**: Still using `Granville.*` package names (e.g., `Granville.Core`, `Granville.Server`)
- **UFX.Orleans.SignalRBackplane**: Re-enabled and working in Shooter.Silo

### Documentation Status
The main CLAUDE.md has been updated to reflect the current hybrid approach with Orleans namespaces for core functionality and Granville.Rpc for RPC-specific code.

## Background
We attempted to use UFX.Orleans.SignalRBackplane but encountered assembly binding issues because:
- UFX is compiled against Orleans.* assemblies (8.2.0)
- Our fork uses Granville.* assemblies (9.2.0.43-preview3)
- Assembly binding redirects in .NET Core/5+ don't fully solve type identity issues

## Planned Approach: Hybrid Orleans/Granville Namespaces

### Short-term
- Disable UFX.Orleans.SignalRBackplane 
- Use standard SignalR without Orleans backplane (works fine for single-silo)

### Long-term Strategy
Change all original Orleans DLL names and namespaces back to Orleans, but keep the RPC DLLs and namespaces as Granville.

**Rationale:**
- Minimal changes were needed to Orleans internals (just some InternalsVisibleTo)
- RPC is a radical departure from main Orleans architecture
- This provides clear separation:
  - **Orleans.*** = Standard Orleans functionality (virtual actors, grains, clustering)
  - **Granville.*** = RPC innovations (game-focused, UDP transport, one-to-many pattern)

**Benefits:**
1. Maximum compatibility with Orleans ecosystem packages (UFX, OrgnalR, etc.)
2. Clear product differentiation for RPC innovation
3. Easier upstream Orleans merges
4. Orleans developers feel at home with familiar namespaces

## Implementation Structure
```
Orleans.Core.dll                 (standard Orleans)
Orleans.Runtime.dll              (standard Orleans)
Orleans.Server.dll               (standard Orleans)
...
Granville.Rpc.Abstractions.dll   (RPC innovation)
Granville.Rpc.Client.dll         (RPC innovation)
Granville.Rpc.Server.dll         (RPC innovation)
Granville.Rpc.Transport.*.dll    (RPC innovation)
```

## Package Naming Options

### Option 1: Keep all as Granville packages (CURRENT CHOICE)
- `Granville.Core` (contains Orleans.Core.dll)
- `Granville.Rpc.Client` (contains Granville.Rpc.Client.dll)

### Option 2: Split package names (FUTURE CONSIDERATION)
- `Forkleans.Core` (contains Orleans.Core.dll)
- `Granville.Rpc.Client` (contains Granville.Rpc.Client.dll)

### Option 3: Single meta-package
- `Granville.Orleans.Extended` (references all packages)

## Implementation Steps
1. Revert Orleans namespaces in `/src/Orleans.*` projects
2. Keep Granville namespaces in `/src/Rpc/*` projects
3. Update `Convert-OrleansNamespace.ps1` to only convert RPC-related Orleans references
4. Update InternalsVisibleTo attributes as needed
5. Re-enable UFX.Orleans.SignalRBackplane
6. Test with third-party Orleans packages

## Progress on Implementation
- [x] Step 1: Revert Orleans namespaces (COMPLETED - 1776 files converted)
- [x] Step 2: Keep Granville namespaces in RPC (ALREADY IN PLACE)
- [x] Step 3: Update conversion script (COMPLETED - Created Revert-GranvilleToOrleans.ps1)
- [x] Step 4: Update InternalsVisibleTo (COMPLETED - Updated to use Granville.Rpc assembly names)
- [x] Step 5: Re-enable UFX backplane (COMPLETED - Re-enabled in Shooter.Silo)
- [ ] Step 6: Test with third-party packages (IN PROGRESS)

## Next Actions
1. ~~Create a new conversion script that reverts Granville back to Orleans for non-RPC code~~ ✓ COMPLETED
2. ~~Update CLAUDE.md to reflect the actual current state (Granville, not Forkleans)~~ ✓ COMPLETED
3. ~~Test the hybrid approach with a small subset of projects first~~ ✓ COMPLETED
4. Complete test project conversions (some still have Granville references)
5. Update Shooter sample to use Orleans packages instead of Granville packages
6. Test build with UFX.Orleans.SignalRBackplane enabled

## Documentation Message
"Granville RPC is an extension to Microsoft Orleans that adds real-time game-focused RPC capabilities. It uses standard Orleans for distributed actors while adding UDP transport and one-to-many communication patterns."