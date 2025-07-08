# Compatibility Mechanism Explanation

## Why is it needed?

The compatibility mechanism exists to support two different scenarios:

1. **Building Orleans source projects**: When building the Orleans source code itself (e.g., in `src/`), many projects have interdependencies. Since we renamed assemblies from `Orleans.*` to `Granville.Orleans.*`, project references between Orleans projects would break without a compatibility mechanism.
2. **Supporting existing Orleans tests and samples**: The Orleans repository includes extensive tests and sample projects that reference `Orleans.*` assemblies. Without compatibility copies, all these would need to be modified.

## How does it work?

The mechanism is implemented in `Directory.Build.targets.compatibility`:

1. **After each build**: When a `Granville.Orleans.*` assembly is built, it creates a copy with the original `Orleans.*` name.
2. **Location**: Both assemblies end up in the same output directory.
3. **Content**: The `Orleans.*` copy is identical to the `Granville.Orleans.*` assembly.

**For example**:
- Building `Orleans.Core.csproj` produces `Granville.Orleans.Core.dll`.
- The compatibility mechanism then copies it to `Orleans.Core.dll`.
- Both files exist in the `bin` directory.

## Impact on Orleans upstream files

The compatibility mechanism has minimal impact on upstream Orleans files:

1. **No source code changes**: The C# source files in `src/` are unchanged.
2. **Minor project file changes**: Only `AssemblyName` properties are overridden via `Directory.Build.targets`.
3. **Build-time only**: All renaming happens during the build process.
4. **Reversible**: Removing `Directory.Build.targets` would restore original behavior.

The main modifications to upstream are:
- Added `AssemblyInfo.Granville.cs` files for `InternalsVisibleTo` attributes.
- `Directory.Build.*` files at the repository root.
- No changes to the actual Orleans implementation code.