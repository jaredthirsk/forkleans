# Minimal Shims with Assembly Redirects Approach

## Overview
This approach combines:
1. **Minimal shim packages** for the 5 modified assemblies (for compile-time)
2. **Assembly redirects** to handle runtime type loading

## How It Works

### Compile Time
- Use shim packages (Microsoft.Orleans.*-granville-shim) for Core, Runtime, Serialization, etc.
- These shims contain type-forwarding to Granville assemblies
- Allows code to compile against Microsoft.Orleans types

### Runtime
- AssemblyRedirectHelper intercepts assembly load requests
- When code tries to load Orleans.Serialization.dll, it redirects to Granville.Orleans.Serialization.dll
- This solves the TypeManifestProviderBase issue where generated code needs actual types

## Implementation Steps

1. **Directory.Packages.props**
   - Shim versions for the 5 modified assemblies + CodeGenerator + Sdk
   - Official versions for Server, Client, etc.

2. **Project Files**
   - Explicitly reference the shim packages for the 5 assemblies
   - This forces transitive dependencies to use shim versions

3. **Enable AssemblyRedirectHelper**
   ```csharp
   Shooter.Shared.AssemblyRedirectHelper.Initialize();
   Shooter.Shared.AssemblyRedirectHelper.PreloadGranvilleAssemblies();
   ```

## Benefits
- Minimal number of shim packages needed
- Handles both compile-time and runtime scenarios
- Works with generated code that inherits from Orleans types

## Limitations
- Requires assembly redirect code to be initialized early
- May have performance overhead for assembly resolution
- Debugging can be more complex due to redirects

## Testing
Run the Shooter.Silo and check:
1. No TypeManifestProviderBase errors
2. Assembly redirect logs show Orleans.* -> Granville.Orleans.* redirects
3. Application functions normally