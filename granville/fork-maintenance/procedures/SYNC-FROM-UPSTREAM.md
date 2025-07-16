# Orleans Fork Upstream Sync Procedures

This document provides comprehensive procedures for syncing the Granville Orleans fork with upstream Microsoft Orleans releases.

## Current Status

- **Current Orleans Base Version**: 9.1.2
- **Current Granville Revision**: 81
- **Current Full Version**: 9.1.2.81
- **Target Orleans Version**: 9.2.0
- **Repository**: [Microsoft Orleans](https://github.com/dotnet/orleans)

## Overview

The Granville Orleans fork maintains a minimal set of changes to upstream Orleans to support:
1. **Assembly Renaming**: Microsoft.Orleans.* → Granville.Orleans.*
2. **Granville RPC Integration**: Additional RPC functionality under `/src/Rpc/`
3. **Compatibility Shims**: Type-forwarding for Microsoft.Orleans package compatibility

### Minimal Impact Strategy

The fork maintains minimal changes to upstream files by:
1. Using separate `AssemblyInfo.Granville.cs` files for Granville-specific InternalsVisibleTo attributes
2. Using `Directory.Build.targets` for build-time assembly renaming without modifying individual project files
3. Keeping all Granville-specific tools and scripts in separate directories
4. Using assembly redirects at runtime rather than modifying source code

This strategy minimizes merge conflicts during upgrades and makes it easier to track our changes.

**For detailed information on current modifications**, see [MODIFICATIONS-TO-UPSTREAM.md](../MODIFICATIONS-TO-UPSTREAM.md) which maintains a comprehensive inventory of all changes made to upstream Orleans files.

## Pre-Upgrade Assessment

Before upgrading to a new Orleans version, perform these assessment steps:

### 1. Review Current Modifications

Check the current list of modifications to understand what needs to be preserved:

```bash
# Review tracked modifications
cat /mnt/g/forks/orleans/granville/fork-maintenance/MODIFICATIONS-TO-UPSTREAM.md

# Run automated upstream changes assessment
cd /mnt/g/forks/orleans
./granville/compatibility-tools/Assess-UpstreamChanges.ps1
```

The assessment script will:
1. Compare against the upstream Orleans repository
2. Identify all added, modified, and deleted files in src/ (excluding src/Rpc)
3. Generate a detailed markdown report
4. Help track our minimal impact on the Orleans codebase

### 2. Check Orleans Release Notes

Review the Orleans release notes for the target version:
- Visit https://github.com/dotnet/orleans/releases
- Look for breaking changes that might affect our fork
- Note any new assemblies or significant architectural changes

### 3. Backup Current State

```bash
# Create a backup branch
git checkout -b backup-pre-upgrade-$(date +%Y%m%d)
git push origin backup-pre-upgrade-$(date +%Y%m%d)

# Return to main branch
git checkout main
```

## Upgrade Procedure

### Step 1: Configure Upstream Remote

```bash
# Add upstream remote if not already present
git remote add upstream https://github.com/dotnet/orleans.git
git fetch upstream
```

### Step 2: Create Upgrade Branch

```bash
# Create upgrade branch
git checkout -b upgrade-orleans-9.2.0
```

### Step 3: Run Upstream Sync Script

```bash
# Execute the automated upstream sync
cd /mnt/g/forks/orleans/granville/scripts
./Update-Fork.ps1 -Branch main
```

**What this script does:**
- Fetches latest upstream changes
- Creates a dated update branch
- Merges upstream changes
- Handles merge conflicts by accepting upstream version and re-applying namespace changes

### Step 4: Handle Merge Conflicts

If merge conflicts occur, the script will:
1. Accept upstream versions of conflicted files
2. Re-apply namespace conversions automatically
3. You may need to manually resolve complex conflicts

**Manual conflict resolution:**
```bash
# View conflicts
git status

# For each conflicted file, choose appropriate resolution
git add <resolved-file>

# Continue merge
git commit
```

### Step 5: Apply Fork Fixes

Run the comprehensive fork fixing script:

```bash
cd /mnt/g/forks/orleans/granville/scripts
./Fix-Fork.ps1 -RootPath /mnt/g/forks/orleans -BackupFirst:$true
```

**What this script does:**
1. Converts namespaces (Orleans → Granville)
2. Fixes project references
3. Updates PackageId values
4. Fixes assembly names
5. Handles F# namespace issues
6. Updates SDK targets
7. Applies special project fixes
8. Cleans build artifacts
9. Restores and builds to verify

### Step 6: Update Version Information

```bash
# Update to Orleans 9.2.0 with revision 1
cd /mnt/g/forks/orleans/granville/scripts
./bump-granville-version.ps1 -VersionPart Full -NewVersion 9.2.0.1
```

**What this script updates:**
- `Directory.Build.props` - VersionPrefix and GranvilleRevision
- `granville/current-revision.txt`
- Related version files

### Step 7: Build and Test

```bash
# Build all Granville components
cd /mnt/g/forks/orleans/granville/scripts
./build-all-granville.ps1

# Alternative: Build Orleans assemblies with Granville prefix
./build-granville-orleans.ps1

# Test core Orleans functionality
cd /mnt/g/forks/orleans
dotnet test Orleans.sln -c Release --no-build

# Test Granville RPC functionality
cd /mnt/g/forks/orleans/granville/samples/Rpc
./test-multi-silo-chat.ps1
```

**Build Script Options:**
- `./build-all-granville.ps1` - Builds all Granville components (Orleans + RPC)
- `./build-granville-orleans.ps1` - Builds only Orleans assemblies with Granville prefix

## Post-Upgrade Validation

### 1. Verify Minimal Changes

Ensure the upgrade maintains our minimal impact strategy:

```bash
# Run upstream changes assessment again
./granville/compatibility-tools/Assess-UpstreamChanges.ps1

# Compare against pre-upgrade assessment
# Should show similar number of modifications
```

### 2. Test Compatibility

```bash
# Test Orleans compatibility
cd /mnt/g/forks/orleans/granville/test/test-ufx-integration
dotnet run

# Test shim packages
cd /mnt/g/forks/orleans/granville/compatibility-tools
./test-shim-packages.ps1
```

### 3. Build Sample Applications

```bash
# Build Shooter sample with Orleans integration
cd /mnt/g/forks/orleans/granville/samples/Rpc
./build-shooter-sample.ps1
```

### 4. Update Documentation

```bash
# Update this document with any new findings
# Update MODIFICATIONS-TO-UPSTREAM.md with new modifications inventory
# Update any version-specific documentation
# Re-run assessment to verify changes are properly tracked
cd /mnt/g/forks/orleans
./granville/compatibility-tools/Assess-UpstreamChanges.ps1
```

## Orleans 9.2.0 Specific Considerations

### Expected Changes

Based on Orleans release patterns, version 9.2.0 typically includes:
- Performance improvements
- Bug fixes
- Minor API additions
- Possible new NuGet packages

### Potential Issues

1. **New Assembly Dependencies**: Check if Orleans 9.2.0 introduces new assemblies that need Granville equivalents
2. **Breaking Changes**: Review release notes for any breaking changes
3. **Code Generation Changes**: Ensure our custom code generation still works
4. **Serialization Updates**: Verify compatibility with our serialization shims

### Merge Conflict Expectations

When syncing with upstream Orleans, expect conflicts in:
1. Modified files listed in [MODIFICATIONS-TO-UPSTREAM.md](../MODIFICATIONS-TO-UPSTREAM.md) may have merge conflicts
2. New files added by this fork will not conflict
3. The assembly info files have been restored to their original state, reducing conflicts
4. The main point of conflict will be `Directory.Build.targets` if upstream adds one

### Migration Strategy

If breaking changes are found:
1. **Assess Impact**: Determine if changes affect our fork
2. **Update Shims**: Modify compatibility shims if needed
3. **Update Scripts**: Enhance build scripts to handle new scenarios
4. **Test Thoroughly**: Extended testing of all integration points

## Key Scripts and Their Functions

### Core Upgrade Scripts
- **`Update-Fork.ps1`**: Handles upstream merging and initial conflict resolution
- **`Fix-Fork.ps1`**: Comprehensive post-merge fixes and namespace conversions
- **`bump-granville-version.ps1`**: Version management and updates

### Assessment and Validation
- **`Assess-UpstreamChanges.ps1`**: Analyzes differences from upstream
- **`build-all-granville.ps1`**: Builds all Granville components
- **`test-shim-packages.ps1`**: Tests compatibility package functionality

### Specialized Fix Scripts
- **`Convert-OrleansNamespace-Fixed.ps1`**: Namespace conversion utility
- **`Fix-AllProjectReferences.ps1`**: Project reference correction
- **`Fix-PackageIds.ps1`**: Package ID updates
- **`Fix-AssemblyNames.ps1`**: Assembly name corrections

## Troubleshooting

### Common Issues

1. **Build Failures After Upgrade**
   - Check for new upstream dependencies
   - Verify namespace conversions completed
   - Review Directory.Build.targets for conflicts

2. **Shim Package Issues**
   - Rebuild shim packages: `./package-minimal-shims.ps1`
   - Test type forwarding: `./test-type-forwards.cs`

3. **Code Generation Problems**
   - Verify Orleans_DesignTimeBuild properties
   - Check analyzer package references
   - Rebuild code generator packages

### Recovery Procedures

If upgrade fails:
```bash
# Return to backup branch
git checkout backup-pre-upgrade-$(date +%Y%m%d)

# Reset main branch
git checkout main
git reset --hard backup-pre-upgrade-$(date +%Y%m%d)

# Restart upgrade process with additional caution
```

## Commit and Release

### Commit Changes

```bash
# Review all changes
git status
git diff --stat

# Commit with descriptive message
git commit -m "Upgrade Orleans from 9.1.2 to 9.2.0

- Merged upstream Orleans 9.2.0 changes
- Applied Granville namespace conversions
- Updated version to 9.2.0.1
- Verified compatibility with existing integrations

🤖 Generated with [Claude Code](https://claude.ai/code)

Co-Authored-By: Claude <noreply@anthropic.com>"
```

### Build Release Packages

```bash
# Build and package all components
cd /mnt/g/forks/orleans/granville/scripts
./build-granville-orleans-packages.ps1
./build-granville-rpc-packages.ps1
./package-minimal-shims.ps1
```

### Push Changes

```bash
# Push to remote
git push origin main

# Create release tag
git tag -a v9.2.0.1 -m "Orleans 9.2.0 upgrade - Granville revision 1"
git push origin v9.2.0.1
```

## Maintenance Notes

- **Frequency**: Plan to upgrade with each Orleans release (typically quarterly)
- **Documentation**: Update this document with lessons learned from each upgrade
- **Testing**: Maintain comprehensive test suite for critical functionality
- **Compatibility**: Always test backward compatibility with existing consumers

## References

- [Orleans Repository](https://github.com/dotnet/orleans)
- [Orleans Release Notes](https://github.com/dotnet/orleans/releases)
- [Granville Repository Organization](../REPO-ORGANIZATION.md)
- [Modifications to Upstream](../MODIFICATIONS-TO-UPSTREAM.md)
- [Building Documentation](../../docs/BUILDING.md)