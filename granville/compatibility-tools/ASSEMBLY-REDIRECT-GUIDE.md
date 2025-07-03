# Assembly Redirect Guide for Granville Orleans

This guide explains how to use assembly binding redirects to make third-party Orleans packages work with Granville Orleans assemblies.

## Overview

Since Granville Orleans builds assemblies with `Granville.Orleans.*` names instead of `Microsoft.Orleans.*`, you need to configure assembly binding redirects in your application to map references from Microsoft assemblies to Granville assemblies.

## Option 1: Using App.config (Recommended for .NET Framework)

1. Copy the `assembly-redirects-template.config` file to your project
2. Merge the `<runtime>` section into your existing `app.config` or `web.config`
3. Adjust version numbers if needed

## Option 2: Using Runtime Configuration (.NET Core/.NET 5+)

For modern .NET applications, create a `[YourApp].runtimeconfig.json` file:

```json
{
  "runtimeOptions": {
    "configProperties": {
      "System.Runtime.Loader.AssemblyLoadContext.Default.ResolvingUnmanagedDll": false
    },
    "additionalProbingPaths": [
      "."
    ]
  }
}
```

Then use a custom assembly resolver in your application startup:

```csharp
using System.Reflection;
using System.Runtime.Loader;

// Add this to your Program.cs or startup code
AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
{
    if (assemblyName.Name.StartsWith("Microsoft.Orleans"))
    {
        var granvilleName = assemblyName.Name.Replace("Microsoft.Orleans", "Granville.Orleans");
        try
        {
            return context.LoadFromAssemblyName(new AssemblyName(granvilleName));
        }
        catch
        {
            // Fallback to default resolution
        }
    }
    return null;
};
```

## Option 3: Using MSBuild PackageReference Aliases

In your `.csproj` file, you can use aliases to redirect at compile time:

```xml
<ItemGroup>
  <PackageReference Include="Granville.Orleans.Core" Version="9.1.2-granville">
    <Aliases>Microsoft.Orleans.Core</Aliases>
  </PackageReference>
</ItemGroup>
```

## Testing Your Configuration

To verify redirects are working:

1. Enable assembly binding logging:
   ```xml
   <configuration>
     <system.diagnostics>
       <trace autoflush="true">
         <listeners>
           <add name="etw" type="System.Diagnostics.Eventing.EventProviderTraceListener, System.Core" initializeData="{GUID-HERE}"/>
         </listeners>
       </trace>
     </system.diagnostics>
   </configuration>
   ```

2. Use Fusion Log Viewer (fuslogvw.exe) on Windows

3. Check the application's debug output for assembly resolution messages

## Common Issues

### Issue: TypeLoadException or FileNotFoundException
**Solution**: Ensure all Granville.Orleans assemblies are in the application's bin directory or GAC

### Issue: Version mismatch errors
**Solution**: Update the version numbers in the binding redirects to match your Granville Orleans version

### Issue: Redirects not being applied
**Solution**: For .NET Core/5+, ensure you're using the assembly resolver approach, as app.config redirects don't work

## Example: Using with UFX SignalR

Here's a complete example for using UFX.Orleans.SignalRBackplane with Granville Orleans:

1. Install packages:
   ```bash
   dotnet add package Granville.Orleans.Core --version 9.1.2-granville
   dotnet add package Granville.Orleans.Runtime --version 9.1.2-granville
   dotnet add package UFX.Orleans.SignalRBackplane
   ```

2. Add assembly resolver to Program.cs (see Option 2 above)

3. Configure Orleans as normal - the redirects handle the namespace mapping transparently

## Alternative: Type Forwarding Shims

If assembly redirects don't work for your scenario, you can use the type forwarding shims:

1. Generate shims using the provided PowerShell script:
   ```powershell
   ./tools/GenerateAllShims.ps1
   ```

2. Deploy both the shim assemblies (Microsoft.Orleans.*) and Granville assemblies together

3. The shims will forward all types to the Granville assemblies automatically

## Hybrid Package Approach (Recommended)

For the best balance of compatibility and maintainability, use the hybrid package approach that combines official Microsoft packages with shims and Granville packages.

### Understanding the Hybrid Strategy

The Granville fork only modifies specific Orleans assemblies to add InternalsVisibleTo attributes. Since InternalsVisibleTo doesn't change binary compatibility, we can use official Microsoft packages for unmodified assemblies.

**Modified assemblies (require Granville versions):**
- Orleans.Core
- Orleans.Core.Abstractions
- Orleans.Runtime
- Orleans.Serialization
- Orleans.Serialization.Abstractions

**Unmodified assemblies (use official Microsoft packages):**
- Orleans.Client
- Orleans.Server
- Orleans.Reminders
- Orleans.Serialization.SystemTextJson
- All other Orleans extensions

### Configuring the Hybrid Approach

1. **Set up Directory.Packages.props:**
   ```xml
   <Project>
     <ItemGroup>
       <!-- Official Microsoft packages for unmodified assemblies -->
       <PackageVersion Include="Microsoft.Orleans.Client" Version="9.1.2" />
       <PackageVersion Include="Microsoft.Orleans.Server" Version="9.1.2" />
       <PackageVersion Include="Microsoft.Orleans.Reminders" Version="9.1.2" />
       <PackageVersion Include="Microsoft.Orleans.Serialization.SystemTextJson" Version="9.1.2" />
       
       <!-- Shim packages for modified assemblies -->
       <PackageVersion Include="Microsoft.Orleans.Core" Version="9.1.2.51-granville-shim" />
       <PackageVersion Include="Microsoft.Orleans.Core.Abstractions" Version="9.1.2.51-granville-shim" />
       <PackageVersion Include="Microsoft.Orleans.Runtime" Version="9.1.2.51-granville-shim" />
       <PackageVersion Include="Microsoft.Orleans.Serialization" Version="9.1.2.51-granville-shim" />
       <PackageVersion Include="Microsoft.Orleans.Serialization.Abstractions" Version="9.1.2.51-granville-shim" />
       
       <!-- Granville packages -->
       <PackageVersion Include="Granville.Rpc.Server" Version="9.1.2.51" />
     </ItemGroup>
   </Project>
   ```

2. **Configure NuGet sources:**
   ```xml
   <configuration>
     <packageSources>
       <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
       <add key="local" value="./Artifacts/Release" />
     </packageSources>
   </configuration>
   ```

3. **Install required packages in your local feed:**
   - Shim packages: Microsoft.Orleans.*-granville-shim
   - Granville packages: Granville.Orleans.* (only for modified assemblies)

### How the Hybrid Approach Works

```
Microsoft.Orleans.Reminders.dll (official from nuget.org)
    ↓ depends on
Microsoft.Orleans.Runtime.dll (shim from local feed)
    ↓ type forwards to
Granville.Orleans.Runtime.dll (actual implementation)
```

### Benefits of the Hybrid Approach

1. **Minimal maintenance**: Only maintain packages for modified assemblies
2. **Maximum compatibility**: Third-party packages work seamlessly
3. **Automatic updates**: Benefit from official package updates
4. **Clear separation**: Easy to see which assemblies are modified

### Troubleshooting Hybrid Setup

1. **Verify package resolution:**
   ```bash
   dotnet list package --include-transitive
   ```

2. **Check assembly versions in bin folder:**
   - Should see Microsoft.Orleans.*.dll for unmodified assemblies
   - Should see Granville.Orleans.*.dll for modified assemblies
   - Shim Microsoft.Orleans.*.dll files should be very small (~10KB)

3. **Common issues:**
   - Missing local feed configuration
   - Version conflicts between official and shim packages
   - Incorrect package source priority

For more details on the hybrid package strategy, see `/granville/docs/hybrid-package-strategy.md`.