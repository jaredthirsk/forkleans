# Forkleans - Orleans Fork Maintenance Guide

## Overview
This guide helps you maintain a fork of Microsoft Orleans with renamed namespaces (Orleans → Forkleans) while keeping it synchronized with upstream changes.

## Initial Setup

### 1. Run the Namespace Converter
```powershell
.\Convert-OrleansNamespace.ps1 -RootPath "C:\path\to\orleans-fork" -DryRun
# Review changes, then run without -DryRun
.\Convert-OrleansNamespace.ps1 -RootPath "C:\path\to\orleans-fork"
```

### 2. Configure Git for Better Merging

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

### 3. Set Up Custom Merge Driver

Add to `.git/config`:
```ini
[merge "namespace"]
    name = Namespace-aware merge
    driver = powershell.exe %O %A %B %L
```

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

## Keeping Your Fork Updated

### 1. Set Up Upstream Remote
```bash
git remote add upstream https://github.com/dotnet/orleans.git
git fetch upstream
```

### 2. Create Update Branch
```bash
git checkout -b update-from-upstream
git merge upstream/main
```

### 3. Handle Conflicts
For namespace conflicts, you'll need to:
1. Accept upstream changes
2. Re-run the namespace converter on conflicted files
3. Manually review and fix any complex conflicts

### 4. Automated Update Script
Create `Update-Fork.ps1`:
```powershell
param(
    [string]$Branch = "main"
)

# Fetch upstream
git fetch upstream

# Create update branch
$updateBranch = "update-$(Get-Date -Format 'yyyyMMdd')"
git checkout -b $updateBranch

# Merge upstream
$mergeResult = git merge upstream/$Branch

if ($LASTEXITCODE -ne 0) {
    Write-Host "Merge conflicts detected. Running namespace converter on conflicted files..."
    
    # Get conflicted files
    $conflicts = git diff --name-only --diff-filter=U
    
    foreach ($file in $conflicts) {
        if ($file -match '\.(cs|csproj)$') {
            # Accept upstream version
            git checkout --theirs $file
            
            # Re-apply namespace changes
            .\Convert-OrleansNamespace.ps1 -RootPath $file -DryRun:$false
        }
    }
}
```

## Best Practices

### 1. File Naming
- **Keep original filenames** containing "Orleans" unchanged
- This makes merging much easier
- Only change namespaces and class names inside files

### 2. Version Control
- Tag your fork versions: `forkleans-v1.0.0`
- Keep a CHANGELOG.md for fork-specific changes
- Document any additional modifications beyond namespace changes

### 3. Testing Strategy
```xml
<!-- In test projects, add namespace mapping -->
<ItemGroup>
  <Using Include="Orleans" Alias="Forkleans" />
</ItemGroup>
```

### 4. NuGet Package Publishing
If publishing your fork as NuGet packages:
```xml
<PropertyGroup>
  <PackageIdPrefix>Forkleans.</PackageIdPrefix>
  <Version>$(Version)-fork</Version>
  <PackageProjectUrl>https://github.com/yourusername/forkleans</PackageProjectUrl>
</PropertyGroup>
```

### 5. Integration Testing
Create a test project that references both Orleans and Forkleans:
```csharp
extern alias Orleans;
extern alias Forkleans;

using OrleansClient = Orleans::Orleans.Client;
using ForkleansClient = Forkleans::Forkleans.Client;
```

## Troubleshooting

### Common Issues

1. **Assembly Binding Redirects**
   - Update `app.config` and `web.config` files
   - Use different version numbers for Forkleans assemblies

2. **Strong Name Signing**
   - Generate new SNK file for Forkleans
   - Update all projects to use the new key

3. **Package Dependencies**
   - Update all inter-package dependencies
   - Consider using `<ProjectReference>` during development

### Conflict Resolution Strategies

1. **Namespace-only changes**: Accept upstream, re-run converter
2. **Logic changes**: Manually merge, then fix namespaces
3. **New files**: Accept fully, then convert
4. **Deleted files**: Accept deletion if file was unchanged

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
      - name: Run update script
        run: .\Update-Fork.ps1
      - name: Create Pull Request
        uses: peter-evans/create-pull-request@v5
        with:
          title: "Sync with upstream Orleans"
          body: "Automated sync with upstream changes"
```

## Additional Tools

### 1. Namespace Mapping Generator
Generates a mapping file for debugging:
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

### 2. Binary Compatibility Checker
Verify your fork maintains compatibility:
```csharp
// Use Microsoft.DotNet.ApiCompat.Tool
dotnet tool install -g Microsoft.DotNet.ApiCompat.Tool
apicompat compare Orleans.dll Forkleans.dll
```

## Questions to Consider

1. **Versioning Strategy**: How will you version Forkleans relative to Orleans?
2. **Public API Surface**: Will you maintain 100% API compatibility?
3. **Documentation**: How will you handle XML documentation comments?
4. **Community**: Will you accept external contributions to your fork?
5. **Legal**: Have you reviewed Orleans' license for forking requirements?

Remember to always test thoroughly after each sync with upstream!