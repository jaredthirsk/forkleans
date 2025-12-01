
## Unwanted Divergences (revert)

### Revert these changes from net8.0 to net9.0 when .NET 8 SDK is installed

These files were modified to use net9.0 target framework to work in a devcontainer
environment that only has .NET 9 SDK installed. Once .NET 8 SDK is available, these
should be reverted to net8.0 for compatibility with the upstream Orleans project.

#### Granville-Specific Files (safe to keep as net9.0 if needed):
- **test/Rpc/Orleans.Rpc.Security.Tests/Orleans.Rpc.Security.Tests.csproj**
  - New test project for RPC authorization
  - Currently: `<TargetFramework>net9.0</TargetFramework>`
  - Should revert to: `<TargetFramework>net8.0</TargetFramework>`
  - Location: Granville RPC test project (not upstream Orleans)

#### Granville Build Tool Files (safe to keep as net9.0 if needed):
- **granville/compatibility-tools/type-forwarding-generator/GenerateTypeForwardingAssemblies.csproj**
  - Build tool for generating type-forwarding shim assemblies
  - Currently: `<TargetFramework>net9.0</TargetFramework>`
  - Should revert to: `<TargetFramework>net8.0</TargetFramework>`
  - Location: Granville build tooling (not upstream Orleans)

#### Impact Assessment:
- None of these are upstream Orleans files
- Both files are Granville-specific:
  - GenerateTypeForwardingAssemblies.csproj: Granville build tool
  - Orleans.Rpc.Security.Tests.csproj: Granville RPC test
- Safe to keep as net9.0 temporarily without affecting Orleans upstream
- However, reverting to net8.0 provides better compatibility

#### Related Build Script Changes:
The following scripts were also updated to detect container environments and
use appropriate dotnet commands. These changes should be KEPT as they improve
cross-platform compatibility:
- granville/compatibility-tools/generate-individual-shims.ps1
- granville/compatibility-tools/package-shims-direct.ps1
- granville/scripts/build-granville-full.ps1

#### Action Items:
1. Install .NET 8 SDK in the devcontainer
2. Revert the two csproj files above to net8.0
3. Test that builds still work
4. Remove this section from Granville-FIXME.md
