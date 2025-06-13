# Fork Maintenance Guide

This guide explains how to maintain the Forkleans fork of Microsoft Orleans after merging changes from upstream.

## Overview

Forkleans is a fork of Microsoft Orleans with:
- Renamed namespaces: `Orleans` → `Forkleans`
- Renamed package prefixes: `Microsoft.Orleans.*` → `Forkleans.*`
- Original filenames preserved for easier merging
- Custom RPC transport implementations

## Maintenance Scripts

### Main Script: `Fix-Fork.ps1`

This is the master script that runs all necessary fixes in the correct order:

```powershell
.\Fix-Fork.ps1 -RootPath "G:\forks\orleans" [-DryRun] [-Verbose]
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
Converts all Orleans namespace references to Forkleans in C#, F#, and project files.

#### `Fix-FSharp-Namespaces.ps1`
Specifically handles F# files which use `open` instead of `using` and have different syntax.

#### `Fix-Missing-Usings.ps1`
Safely adds missing `using Forkleans;` statements where needed. This replaces the problematic `fix-using-statements.ps1` script.

#### `Fix-Syntax-Errors.ps1`
Fixes common syntax errors like missing braces that may have been introduced by automated scripts.

#### `Smart-Fix-References.ps1`
Updates project and package references to use Forkleans names.

#### `Fix-AssemblyNames.ps1`
Updates assembly names in project files.

## Workflow After Upstream Merge

1. **Merge from upstream Orleans**
   ```bash
   git checkout update-from-upstream
   git pull upstream main
   ```

2. **Run the fork maintenance script**
   ```powershell
   # First do a dry run to see what will change
   .\Fix-Fork.ps1 -RootPath "G:\forks\orleans" -DryRun

   # If everything looks good, run it for real
   .\Fix-Fork.ps1 -RootPath "G:\forks\orleans"
   ```

3. **Build and test**
   ```bash
   dotnet build Orleans.sln
   dotnet test Orleans.sln
   ```

4. **Review and fix any remaining issues**
   - Check for build errors
   - Look for any Orleans references that weren't converted
   - Test key functionality

5. **Commit the changes**
   ```bash
   git add -A
   git commit -m "Apply fork namespace conversions after upstream merge"
   ```

## Common Issues and Solutions

### Missing Using Statements
The scripts will automatically add missing `using Forkleans;` statements, but some files may need additional namespaces like:
- `using Forkleans.Hosting;`
- `using Forkleans.Runtime;`

### Syntax Errors
If you see syntax errors after running the scripts, check for:
- Missing braces in control structures
- Duplicate `await` keywords
- Malformed method signatures

The `Fix-Syntax-Errors.ps1` script attempts to fix these automatically.

### F# Files
F# files have different syntax and may need manual review, especially for:
- `open` statements instead of `using`
- Attribute syntax `[<AttributeName>]`
- Module and namespace declarations

### Build Failures
If certain projects still fail to build:
1. Check for missing project references
2. Verify all Orleans namespaces were converted
3. Look for hardcoded Orleans references in string literals
4. Check for Orleans references in XML documentation comments

## Testing After Maintenance

Always run these tests after fork maintenance:
1. Basic build: `dotnet build Orleans.sln`
2. Unit tests: `dotnet test Orleans.sln --filter "Category=BVT"`
3. Integration tests: `dotnet test Orleans.sln --filter "Category!=BVT"`
4. RPC tests: Focus on testing the custom RPC implementations

## Important Notes

- **DO NOT** use the old `fix-using-statements.ps1` script - it has been renamed to `.old` because it removes duplicate lines throughout entire files, breaking code syntax
- Always do a dry run first with the `-DryRun` flag
- Keep original Orleans filenames to make merging easier
- The scripts preserve git history by modifying files in place
- Some manual fixes may still be needed, especially in complex scenarios

## Script Development

When creating new maintenance scripts:
1. Always include a `-DryRun` option
2. Process files safely - don't remove arbitrary duplicate lines
3. Handle both C# and F# syntax appropriately  
4. Validate changes (e.g., check brace balance)
5. Provide clear output about what was changed
6. Handle locked files gracefully