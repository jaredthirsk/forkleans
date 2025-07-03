# Hybrid Package Strategy for Granville Orleans Fork

## Overview

This document explains the hybrid package strategy used in the Granville Orleans fork, which combines official Microsoft Orleans packages, type-forwarding shim packages, and Granville-built packages to minimize maintenance burden while maintaining full compatibility.

## The Challenge

When creating a fork of Orleans that adds InternalsVisibleTo attributes for Granville.Rpc, we face several challenges:
1. We need specific assemblies to expose internals to Granville.Rpc
2. Third-party packages (like UFX.Orleans.SignalRBackplane) expect Microsoft.Orleans assemblies
3. We want to minimize the number of packages we need to maintain
4. We need to ensure binary compatibility across all components

## Key Insight: InternalsVisibleTo Preserves Binary Compatibility

The critical insight is that adding InternalsVisibleTo attributes to an assembly does NOT break binary compatibility:

- **What InternalsVisibleTo changes**: Assembly metadata that allows specified assemblies to access internal members
- **What InternalsVisibleTo does NOT change**:
  - Type identities (namespaces, class names)
  - Public API surface (methods, properties, fields)
  - Assembly strong names (when not signed)
  - Type layouts or vtables
  - Method signatures or calling conventions

This means an assembly compiled against the original Microsoft.Orleans.Core will work perfectly with our modified Granville.Orleans.Core at runtime, as long as we handle the assembly name mapping.

## The Hybrid Package Strategy

### 1. Identify Modified Assemblies

Based on `/granville/fork-maintenance/MODIFICATIONS-TO-UPSTREAM.md`, we only added InternalsVisibleTo to:
- Orleans.Core
- Orleans.Core.Abstractions
- Orleans.Runtime
- Orleans.Serialization
- Orleans.Serialization.Abstractions

### 2. Package Distribution Strategy

#### Official Microsoft Packages (Unmodified)
Use official NuGet packages for assemblies we didn't modify:
- Microsoft.Orleans.Client (9.1.2)
- Microsoft.Orleans.Server (9.1.2)
- Microsoft.Orleans.Reminders (9.1.2)
- Microsoft.Orleans.Reminders.Abstractions (9.1.2)
- Microsoft.Orleans.Serialization.SystemTextJson (9.1.2)
- Microsoft.Orleans.Persistence.Memory (9.1.2)
- Any other Orleans extensions

#### Type-Forwarding Shim Packages
Use shim packages that forward Microsoft.Orleans.* types to Granville.Orleans.*:
- Microsoft.Orleans.Core (9.1.2.51-granville-shim)
- Microsoft.Orleans.Core.Abstractions (9.1.2.51-granville-shim)
- Microsoft.Orleans.Runtime (9.1.2.51-granville-shim)
- Microsoft.Orleans.Serialization (9.1.2.51-granville-shim)
- Microsoft.Orleans.Serialization.Abstractions (9.1.2.51-granville-shim)

#### Granville Orleans Packages
Build and maintain only the modified assemblies:
- Granville.Orleans.Core (9.1.2.51)
- Granville.Orleans.Core.Abstractions (9.1.2.51)
- Granville.Orleans.Runtime (9.1.2.51)
- Granville.Orleans.Serialization (9.1.2.51)
- Granville.Orleans.Serialization.Abstractions (9.1.2.51)

### 3. How It Works

When Microsoft.Orleans.Reminders (official) runs:
1. It has a dependency on Microsoft.Orleans.Runtime
2. NuGet resolves this to Microsoft.Orleans.Runtime (9.1.2.51-granville-shim)
3. The shim assembly forwards all types to Granville.Orleans.Runtime
4. Granville.Orleans.Runtime provides the actual implementation with InternalsVisibleTo

```
Microsoft.Orleans.Reminders.dll (official)
    ↓ depends on
Microsoft.Orleans.Runtime.dll (shim)
    ↓ type forwards to
Granville.Orleans.Runtime.dll (actual implementation)
```

## Benefits of the Hybrid Approach

### 1. Minimal Maintenance Burden
- Only maintain 5 Granville packages instead of 20+
- Automatically benefit from updates to unmodified packages
- Reduced build complexity and time

### 2. Maximum Compatibility
- Third-party packages work without modification
- Both Microsoft.Orleans and Granville.Orleans namespaces supported
- No source code changes required in consuming applications

### 3. Clear Separation of Concerns
- Modified assemblies are clearly identified
- Unmodified assemblies use official packages
- Shims provide the compatibility layer

### 4. Future-Proof
- Easy to add more modifications to specific assemblies
- Can gradually migrate more assemblies if needed
- Simple to revert to full Microsoft.Orleans if upstream accepts changes

## Implementation Guidelines

### For Application Developers

1. **Configure Package Sources**:
   ```xml
   <packageSources>
     <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
     <add key="local" value="./path/to/local/feed" />
   </packageSources>
   ```

2. **Use Directory.Packages.props**:
   ```xml
   <!-- Official packages -->
   <PackageVersion Include="Microsoft.Orleans.Reminders" Version="9.1.2" />
   
   <!-- Shim packages from local feed -->
   <PackageVersion Include="Microsoft.Orleans.Core" Version="9.1.2.51-granville-shim" />
   
   <!-- Granville packages -->
   <PackageVersion Include="Granville.Rpc.Server" Version="9.1.2.51" />
   ```

### For Fork Maintainers

1. **Build only modified assemblies**: Run `build-orleans-packages.ps1` for the 5 modified projects
2. **Generate shims**: Run `Generate-TypeForwardingShims.ps1` after building
3. **Test compatibility**: Use `validate-hybrid-packages.ps1` to verify setup

## Troubleshooting

### Common Issues

1. **Package not found**: Ensure both nuget.org and local feeds are configured
2. **Type load exceptions**: Verify shim packages are installed for all modified assemblies
3. **Version conflicts**: Use exact versions in Directory.Packages.props

### Validation Checklist

- [ ] Official packages resolve from nuget.org
- [ ] Shim packages resolve from local feed
- [ ] Granville packages exist for all modified assemblies
- [ ] No Microsoft.Orleans.Core.dll in bin folder (only shim)
- [ ] Granville.Orleans.Core.dll exists in bin folder

## Future Considerations

1. **Upstream Contribution**: If Orleans accepts InternalsVisibleTo changes, we can deprecate this fork
2. **Additional Modifications**: If more assemblies need modification, update this strategy
3. **Version Alignment**: Keep version numbers synchronized with Orleans releases

## Conclusion

The hybrid package strategy provides the best balance between compatibility, maintainability, and functionality. By leveraging binary compatibility of InternalsVisibleTo changes and type-forwarding shims, we can use official packages wherever possible while maintaining the modifications needed for Granville.Rpc.