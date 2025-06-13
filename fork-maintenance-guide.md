# Forkleans - Orleans Fork Maintenance Guide

## Overview

Forkleans is a fork of Microsoft Orleans with:
- Renamed namespaces: `Orleans` → `Forkleans`
- Renamed package prefixes: `Microsoft.Orleans.*` → `Forkleans.*`
- Original filenames preserved for easier merging
- Custom RPC transport implementations

This guide explains how to maintain the fork after merging changes from upstream Orleans.

## Quick Start - Maintenance Workflow

After merging from upstream Orleans:

```powershell
# First do a dry run to see what will change
.\Fix-Fork.ps1 -RootPath "G:\forks\orleans" -DryRun

# If everything looks good, run it for real
.\Fix-Fork.ps1 -RootPath "G:\forks\orleans"

# Build and test
dotnet build Orleans.sln
dotnet test Orleans.sln
```

## Maintenance Scripts

### Main Script: `Fix-Fork.ps1`

This is the master script that runs all necessary fixes in the correct order:

```powershell
.\Fix-Fork.ps1 -RootPath "G:\forks\orleans" [-DryRun]
```

It performs the following steps:
1. Converts namespaces from Orleans to Forkleans
2. Fixes project references
3. Fixes assembly names
4. Fixes F# specific namespace issues
5. Adds missing using statements
6. Fixes syntax errors
7. Builds the solution and reports results

### Individual Scripts

#### `Convert-OrleansNamespace.ps1`
- Converts all Orleans namespace references to Forkleans in C#, F#, and project files
- Handles `using`, `namespace`, and fully qualified type references
- Updates project files (`.csproj`, `.fsproj`)
- Preserves file names for easier merging

#### `Fix-FSharp-Namespaces.ps1`
- Specifically handles F# files which use `open` instead of `using`
- Fixes F# attribute syntax `[<AttributeName>]`
- Updates F# project files

#### `Fix-Missing-Usings.ps1`
- Safely adds missing `using Forkleans;` statements where needed
- Also adds `using Forkleans.Hosting;` and `using Forkleans.Runtime;` as needed
- **Important**: This replaces the problematic `fix-using-statements.ps1` script that had a critical bug

#### `Fix-Syntax-Errors.ps1`
- Fixes common syntax errors like missing braces
- Fixes double `await` patterns
- Validates brace balance
- Reports files that may still have issues

#### `Smart-Fix-References.ps1`
- Updates project and package references to use Forkleans names
- Handles `<ProjectReference>` and `<PackageReference>` elements

#### `Fix-AssemblyNames.ps1`
- Updates assembly names in project files
- Changes `<AssemblyName>`, `<RootNamespace>`, `<PackageId>`, and `<Product>` tags

## Initial Setup

### 1. Configure Git for Better Merging

Create `.gitattributes` in your fork root:
```gitattributes
# Treat these files as binary to avoid merge conflicts
*.dll binary
*.exe binary
*.pdb binary

# Use union merge for project files
*.csproj merge=union
*.sln merge=union

# Custom merge driver for C# files
*.cs merge=ours
```

### 2. Set Up Custom Merge Driver

Add to `.git/config`:
```ini
[merge "namespace"]
    name = Namespace-aware merge
    driver = powershell.exe %O %A %B %L
```

### 3. Set Up Upstream Remote
```bash
git remote add upstream https://github.com/dotnet/orleans.git
git fetch upstream
```

## Complete Workflow After Upstream Merge

1. **Create update branch and merge from upstream**
   ```bash
   git checkout main
   git pull origin main
   git checkout -b update-from-upstream
   git fetch upstream
   git merge upstream/main
   ```

2. **Handle merge conflicts**
   - For namespace conflicts: Accept upstream changes, then re-run converters
   - For logic changes: Manually merge, then fix namespaces
   - For new files: Accept fully, then convert

3. **Run the fork maintenance script**
   ```powershell
   # Always do a dry run first
   .\Fix-Fork.ps1 -RootPath "G:\forks\orleans" -DryRun

   # Apply the changes
   .\Fix-Fork.ps1 -RootPath "G:\forks\orleans"
   ```

4. **Build and test**
   ```bash
   # Basic build
   dotnet build Orleans.sln

   # Run BVT (Basic Verification Tests)
   dotnet test Orleans.sln --filter "Category=BVT"

   # Run all tests
   dotnet test Orleans.sln
   ```

5. **Review and fix any remaining issues**
   - Check build output for errors
   - Look for any Orleans references that weren't converted
   - Test RPC functionality specifically

6. **Commit the changes**
   ```bash
   git add -A
   git commit -m "Apply fork namespace conversions after upstream merge"
   git push origin update-from-upstream
   ```

7. **Create pull request**
   - Create PR from `update-from-upstream` to `main`
   - Review all changes carefully
   - Ensure CI passes

## Common Issues and Solutions

### Missing Using Statements
The scripts automatically add missing `using` statements for:
- `using Forkleans;` - Core types like `Grain`, `IGrainFactory`
- `using Forkleans.Hosting;` - Types like `ISiloBuilder`
- `using Forkleans.Runtime;` - Types like `RequestContext`, `IManagementGrain`

### Syntax Errors
Common syntax errors after automated processing:
- Missing braces in control structures
- Duplicate `await` keywords (e.g., `await await`)
- Malformed method signatures

The `Fix-Syntax-Errors.ps1` script attempts to fix these automatically.

### F# Files
F# files have different syntax and may need special attention:
- `open` statements instead of `using`
- Attribute syntax `[<AttributeName>]`
- Module and namespace declarations
- Type annotations with `:` syntax

### Build Failures
If certain projects still fail to build:
1. Check for missing project references
2. Verify all Orleans namespaces were converted
3. Look for hardcoded Orleans references in:
   - String literals
   - XML documentation comments
   - Assembly attributes
   - Configuration files

## DLL Naming Strategy

### Approach 1: Modify CSProj Files (Recommended)
The converter script automatically updates:
- `<AssemblyName>` - Changes DLL output name
- `<RootNamespace>` - Changes default namespace
- `<PackageId>` - Changes NuGet package ID
- `<Product>` - Changes product name

### Approach 2: MSBuild Properties
Create `Directory.Build.props` in your fork root:
```xml
<Project>
  <PropertyGroup>
    <AssemblyNamePrefix>Fork</AssemblyNamePrefix>
    <AssemblyName>$(AssemblyNamePrefix)$(AssemblyName.Replace('Orleans', ''))</AssemblyName>
  </PropertyGroup>
</Project>
```

## Best Practices

### 1. File Naming
- **Keep original filenames** containing "Orleans" unchanged
- This makes merging much easier
- Only change namespaces and content inside files

### 2. Version Control
- Tag your fork versions: `forkleans-v1.0.0`
- Keep a CHANGELOG.md for fork-specific changes
- Document any additional modifications beyond namespace changes
- Create a branch for each upstream sync

### 3. Testing Strategy
For test projects that need to reference both namespaces:
```xml
<!-- In test projects, add namespace mapping -->
<ItemGroup>
  <Using Include="Orleans" Alias="Forkleans" />
</ItemGroup>
```

### 4. Important Notes

- Always do a dry run first with the `-DryRun` flag
- Keep original Orleans filenames to make merging easier
- The scripts preserve git history by modifying files in place
- Some manual fixes may still be needed, especially in complex scenarios

## Automation Opportunities

### GitHub Actions Workflow
```yaml
name: Sync with Upstream

on:
  schedule:
    - cron: '0 0 * * 0' # Weekly
  workflow_dispatch:

jobs:
  sync:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v3
      - name: Configure Git
        run: |
          git config user.name "GitHub Actions"
          git config user.email "actions@github.com"
      - name: Add upstream
        run: git remote add upstream https://github.com/dotnet/orleans.git
      - name: Fetch upstream
        run: git fetch upstream
      - name: Create update branch
        run: git checkout -b update-$(Get-Date -Format 'yyyyMMdd')
      - name: Merge upstream
        run: git merge upstream/main --no-edit || true
      - name: Run fork maintenance
        run: .\Fix-Fork.ps1 -RootPath .
      - name: Build solution
        run: dotnet build Orleans.sln
      - name: Create Pull Request
        uses: peter-evans/create-pull-request@v5
        with:
          title: "Sync with upstream Orleans"
          body: "Automated sync with upstream changes"
          branch: update-from-upstream
```

## Script Development Guidelines

When creating new maintenance scripts:
1. Always include a `-DryRun` option
2. Process files safely - don't remove arbitrary duplicate lines
3. Handle both C# and F# syntax appropriately
4. Validate changes (e.g., check brace balance)
5. Provide clear output about what was changed
6. Handle locked files gracefully
7. Test on a small subset first

## Troubleshooting

### Assembly Binding Redirects
- Update `app.config` and `web.config` files
- Use different version numbers for Forkleans assemblies

### Strong Name Signing
- Generate new SNK file for Forkleans
- Update all projects to use the new key

### Package Dependencies
- Update all inter-package dependencies
- Consider using `<ProjectReference>` during development

### Namespace Mapping for Debugging
Generate a mapping file:
```powershell
Get-ChildItem -Recurse -Filter "*.cs" | ForEach-Object {
    $content = Get-Content $_.FullName
    $namespaces = $content | Select-String -Pattern "namespace\s+(\S+)" |
                            ForEach-Object { $_.Matches[0].Groups[1].Value }
    $namespaces | ForEach-Object {
        [PSCustomObject]@{
            Original = $_ -replace "Forkleans", "Orleans"
            Fork = $_
            File = $_.FullName
        }
    }
} | Export-Csv -Path "namespace-mapping.csv"
```

## Questions to Consider

1. **Versioning Strategy**: How will you version Forkleans relative to Orleans?
2. **Public API Surface**: Will you maintain 100% API compatibility?
3. **Documentation**: How will you handle XML documentation comments?
4. **Community**: Will you accept external contributions to your fork?
5. **Legal**: Have you reviewed Orleans' license for forking requirements?

Remember to always test thoroughly after each sync with upstream!
