# Option 2 Test: Assembly Binding Redirects

This test demonstrates how the assembly redirect approach (Option 2) works for making third-party Orleans packages compatible with Granville Orleans.

## Current Status

The test successfully demonstrates:
1. ✅ Assembly redirect handler intercepts requests for Microsoft.Orleans.* assemblies
2. ✅ UFX.Orleans.SignalRBackplane loads and shows its Orleans dependencies
3. ✅ The redirect mechanism attempts to load Granville.Orleans.* when Microsoft.Orleans.* is requested

The full working implementation requires:
- Actual Granville.Orleans assemblies built from the main Orleans source
- These assemblies properly signed and available in the application directory or GAC

## How It Works

1. The program sets up an `AssemblyLoadContext.Default.Resolving` handler
2. When any code requests a `Microsoft.Orleans.*` assembly, the handler intercepts it
3. The handler redirects the request to load the corresponding `Granville.Orleans.*` assembly instead
4. This happens transparently at runtime, so packages like UFX.Orleans.SignalRBackplane work without modification

## Running the Test

```bash
dotnet restore
dotnet run
```

## Expected Output

The program will show:
- Assembly resolution requests being intercepted
- Attempts to redirect Microsoft.Orleans.* to Granville.Orleans.*
- How UFX.Orleans.SignalRBackplane's dependencies are handled

## Real-World Usage

In a real application using Granville Orleans:

1. Install Granville Orleans packages (when published to NuGet)
2. Add the assembly redirect code to your Program.cs (before any Orleans usage)
3. Third-party packages expecting Microsoft.Orleans will transparently use Granville.Orleans

## Benefits

- No need to modify third-party packages
- Works at runtime, no compile-time changes needed
- Transparent to the rest of your application code
- Can be toggled on/off easily

## Limitations

- Only works for .NET Core/.NET 5+ (not .NET Framework)
- Requires the redirect code in every application entry point
- May have slight performance overhead for assembly resolution