# Repository Renaming Plan: forkleans → granville-rpc

This document outlines the steps needed to rename the repository from `forkleans` to `granville-rpc`.

## Current State
- **Current Repository URL**: `https://github.com/jaredthirsk/forkleans.git`
- **Desired Repository URL**: `https://github.com/jaredthirsk/granville-rpc.git`
- **Occurrences of "forkleans"**: 47 files containing references
- **Occurrences in documentation**: 68 references in .md files

## Prerequisites
1. Create the new repository on GitHub: `granville-rpc`
2. Ensure you have push access to the new repository

## Step-by-Step Renaming Process

### 1. Update Git Remote URL
```bash
git remote set-url origin https://github.com/jaredthirsk/granville-rpc.git
```

### 2. Update Documentation Files
Replace all occurrences of "Forkleans" with "Granville RPC" in documentation:

#### Key files to update:
- `/granville/samples/Rpc/README.md`
  - Title: "Forkleans Space Shooter Demo" → "Granville RPC Space Shooter Demo"
  - All references to "Forkleans RPC" → "Granville RPC"
  
- `/granville/samples/Rpc/docs/*.md` - Various documentation files
- `/src/Rpc/docs/*.md` - RPC documentation
- All other .md files containing "forkleans"

### 3. Update Code References
Replace references in source code and project files:

#### PowerShell Scripts
Update references in all `.ps1` files under `/granville/scripts/`

#### C# Source Files
Update any comments or string literals containing "forkleans" in:
- `/test/Rpc/Orleans.Rpc.*/Program.cs`
- Other `.cs` files as identified

#### Project Files
Update any references in `.csproj` files

### 4. Update NuGet Configuration
In NuGet.config files, update:
- Package source names: `LocalForkleans` → `LocalGranvilleRpc`
- Comments: "local Forkleans feed" → "local Granville RPC feed"

Example:
```xml
<!-- Old -->
<add key="LocalForkleans" value="../../../Artifacts/Release" />

<!-- New -->
<add key="LocalGranvilleRpc" value="../../../Artifacts/Release" />
```

### 5. Search and Replace Commands
To find all occurrences:
```bash
# Case-insensitive search
grep -ri "forkleans" . --include="*.md" --include="*.ps1" --include="*.sh" --include="*.cs" --include="*.csproj" --include="*.json" --include="*.xml" --include="*.yml" --include="*.yaml" | grep -v "\.git/"
```

### 6. Verify Changes
After making all replacements:
1. Build the solution to ensure no breaking changes
2. Run tests to verify functionality
3. Review git diff to ensure all changes are intentional

### 7. Commit and Push
```bash
git add -A
git commit -m "Rename repository from forkleans to granville-rpc"
git push origin main
```

## Considerations
- **Case sensitivity**: Be careful with case when replacing (Forkleans vs forkleans)
- **Context**: Ensure replacements make sense in context (e.g., "Forkleans RPC" → "Granville RPC")
- **URLs**: Update any hardcoded GitHub URLs
- **Documentation**: Update any references to the old repository name in documentation

## Post-Rename Tasks
1. Update the old repository on GitHub with a notice pointing to the new location
2. Update any local clones on other machines
3. Update any bookmarks or references to the old repository URL
4. Consider setting up a redirect from the old repository to the new one (GitHub feature)

## Notes
- Since this is currently a single-user, local-only repository, there are no concerns about:
  - Breaking other users' clones
  - Updating CI/CD pipelines
  - Coordinating with team members
- The package names remain as Granville.* (no change needed)
- The fork name itself could potentially change from "Granville Orleans fork" to "Granville RPC" in documentation