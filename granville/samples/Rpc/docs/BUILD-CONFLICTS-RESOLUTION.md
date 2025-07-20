# Shooter Sample Build Conflicts Resolution

## Issue

The Shooter sample experiences build conflicts when using both Granville.Orleans and Microsoft.Orleans packages due to:
1. Assembly name conflicts between Granville and Microsoft Orleans
2. UFX.Orleans.SignalRBackplane dependency on Orleans 8.x conflicting with Granville 9.x
3. Code generation issues with Orleans 9.1.2

## Temporary Resolution

### Changes Made

1. **Removed Microsoft Orleans shim references** to avoid assembly conflicts
2. **Disabled UFX.Orleans.SignalRBackplane** due to Orleans 8.x dependency
3. **Created Directory.Build.targets** to disable code generation for Shooter.Shared
4. **Updated package references** to use only Granville.Orleans packages

### Files Modified

- `Directory.Packages.props` - Removed Microsoft Orleans shim packages
- `Directory.Build.targets` - Added to disable code generation
- `Shooter.Silo/Shooter.Silo.csproj` - Removed UFX and shim packages
- `build-shooter-fixed.ps1` - Created build script with workarounds

## Current Status

The build conflicts are partially resolved but the sample requires:
1. Memory persistence implementation for Granville.Orleans
2. Reminders support in Granville.Orleans
3. Resolution of UFX compatibility issues

## Recommended Approach

For testing RPC functionality:
1. Use the simple SessionIsolationTest in `/src/Rpc/test/`
2. Build individual components with code generation disabled
3. Focus on RPC-specific features without Orleans extensions

## Future Work

- Create Granville.Orleans.Persistence.Memory package
- Add reminders support to Granville packages
- Develop compatibility layer for UFX SignalR integration
- Resolve Orleans 9.1.2 code generation issues