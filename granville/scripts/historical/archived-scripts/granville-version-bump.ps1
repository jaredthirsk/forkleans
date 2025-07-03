#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Bumps the Granville version and optionally packages the RPC libraries.

.DESCRIPTION
    This script automates the process of bumping the Granville version number
    and creating NuGet packages for the RPC libraries.

.PARAMETER VersionPart
    Which part of the version to bump: Major, Minor, Patch, or Revision
    Default: Revision

.PARAMETER SpecificVersion
    Set a specific version instead of bumping (e.g., "9.2.0.5-preview3")

.PARAMETER SkipPackage
    Skip creating NuGet packages
    Default: false

.PARAMETER Configuration
    Build configuration (Debug or Release)
    Default: Release

.PARAMETER Mode
    Package building mode: Essential, RpcTypical, or All
    - Essential: Minimal packages needed for RPC functionality
    - RpcTypical: Common packages for Granville RPC (default)
    - All: All available packages
    Default: RpcTypical

.PARAMETER NoCleanLocalPackages
    Skip cleaning old NuGet packages from ./Artifacts/Release/*.nupkg
    Default: false (packages are cleaned)

.PARAMETER NoCleanArtifacts
    Skip cleaning old NuGet packages from ./Artifacts/{Configuration}/*.nupkg (e.g., ./Artifacts/Release/*.nupkg)
    Default: false (packages are cleaned)

.PARAMETER CleanBuildArtifacts
    Clean all bin and obj folders in the repository before building
    Default: false

.EXAMPLE
    ./Granville-version-bump.ps1
    Bumps the revision number, cleans old packages, and creates typical RPC packages

.EXAMPLE
    ./Granville-version-bump.ps1 -VersionPart Patch
    Bumps the patch number

.EXAMPLE
    ./Granville-version-bump.ps1 -SpecificVersion "9.2.1.0-preview3"
    Sets a specific version

.EXAMPLE
    ./Granville-version-bump.ps1 -SkipPackage
    Only bumps the version without packaging

.EXAMPLE
    ./Granville-version-bump.ps1 -Mode Essential
    Bumps version and creates only essential packages

.EXAMPLE
    ./Granville-version-bump.ps1 -NoCleanLocalPackages -NoCleanArtifacts
    Bumps version and creates packages without cleaning old packages
#>

[CmdletBinding()]
param(
    [ValidateSet("Major", "Minor", "Patch", "Revision")]
    [string]$VersionPart = "Revision",

    [string]$SpecificVersion = "",

    [switch]$SkipPackage = $false,

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("Essential", "RpcTypical", "All")]
    [string]$Mode = "RpcTypical",

    [switch]$NoCleanLocalPackages = $false,

    [switch]$NoCleanArtifacts = $false,

    [switch]$CleanBuildArtifacts = $false
)

$ErrorActionPreference = "Stop"

# Colors for output
function Write-Header($message) {
    Write-Host "`n=== $message ===" -ForegroundColor Cyan
}

function Write-Success($message) {
    Write-Host "✓ $message" -ForegroundColor Green
}

function Write-Error($message) {
    Write-Host "✗ $message" -ForegroundColor Red
}

function Write-Info($message) {
    Write-Host "  $message" -ForegroundColor Gray
}

# Get the script directory
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# Read current version from Directory.Build.props
Write-Header "Reading current version"
$buildPropsPath = Join-Path $scriptDir "Directory.Build.props"

if (-not (Test-Path $buildPropsPath)) {
    Write-Error "Directory.Build.props not found at $buildPropsPath"
    exit 1
}

$buildPropsContent = Get-Content $buildPropsPath -Raw
$versionMatch = $buildPropsContent -match '<Version>([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)(-[^<]+)?</Version>'

if (-not $versionMatch) {
    Write-Error "Could not find version in Directory.Build.props"
    exit 1
}

$currentVersion = $Matches[1]
$versionSuffix = $Matches[2]
Write-Info "Current version: $currentVersion$versionSuffix"

# Determine new version
if ($SpecificVersion) {
    # Use specific version
    if ($SpecificVersion -match '^([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)(-.*)?$') {
        $newVersion = $Matches[1]
        $newSuffix = $Matches[2]
        if (-not $newSuffix -and $versionSuffix) {
            $newSuffix = $versionSuffix
        }
    } else {
        Write-Error "Invalid version format: $SpecificVersion"
        Write-Info "Expected format: Major.Minor.Patch.Revision[-suffix]"
        exit 1
    }
} else {
    # Bump version
    $versionParts = $currentVersion.Split('.')
    $major = [int]$versionParts[0]
    $minor = [int]$versionParts[1]
    $patch = [int]$versionParts[2]
    $revision = [int]$versionParts[3]

    switch ($VersionPart) {
        "Major" {
            $major++
            $minor = 0
            $patch = 0
            $revision = 0
        }
        "Minor" {
            $minor++
            $patch = 0
            $revision = 0
        }
        "Patch" {
            $patch++
            $revision = 0
        }
        "Revision" {
            $revision++
        }
    }

    $newVersion = "$major.$minor.$patch.$revision"
    $newSuffix = $versionSuffix
}

$fullNewVersion = "$newVersion$newSuffix"
Write-Success "New version will be: $fullNewVersion"

# Update Directory.Build.props
Write-Header "Updating Directory.Build.props"
$newBuildPropsContent = $buildPropsContent -replace '<Version>[^<]+</Version>', "<Version>$fullNewVersion</Version>"
Set-Content -Path $buildPropsPath -Value $newBuildPropsContent -NoNewline
Write-Success "Updated Directory.Build.props"

# Update Directory.Packages.props in samples/Rpc if it exists
$rpcPackagesPropsPath = Join-Path $scriptDir "samples/Rpc/Directory.Packages.props"
if (Test-Path $rpcPackagesPropsPath) {
    Write-Header "Updating samples/Rpc/Directory.Packages.props"
    $packagesContent = Get-Content $rpcPackagesPropsPath -Raw

    # Update all Granville package versions
    $packagesContent = $packagesContent -replace '(Include="Granville\.[^"]+"\s+Version=")[^"]+(")', "`${1}$fullNewVersion`${2}"

    Set-Content -Path $rpcPackagesPropsPath -Value $packagesContent -NoNewline
    Write-Success "Updated samples/Rpc/Directory.Packages.props"
}

# Clean build artifacts if requested
if ($CleanBuildArtifacts) {
    Write-Header "Cleaning build artifacts"
    $cleanScript = Join-Path $scriptDir "clean-build-artifacts.ps1"
    if (Test-Path $cleanScript) {
        Write-Info "Running clean-build-artifacts.ps1..."
        & $cleanScript
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to clean build artifacts"
            exit 1
        }
    } else {
        Write-Warning "clean-build-artifacts.ps1 not found, skipping build artifact cleanup"
    }
}

# Clean old packages if requested
if (-not $SkipPackage) {
    # Clean local packages
    if (-not $NoCleanLocalPackages) {
        Write-Header "Cleaning local packages"
        $localPackagesPath = Join-Path $scriptDir "../../../Artifacts/Release"
        if (Test-Path $localPackagesPath) {
            $oldPackages = Get-ChildItem -Path $localPackagesPath -Filter "*.nupkg" -ErrorAction SilentlyContinue
            if ($oldPackages) {
                Write-Info "Removing $($oldPackages.Count) old package(s) from Artifacts/Release"
                Remove-Item -Path (Join-Path $localPackagesPath "*.nupkg") -Force
                Write-Success "Cleaned local packages"
            } else {
                Write-Info "No packages to clean in Artifacts/Release"
            }
        } else {
            Write-Info "Artifacts/Release directory does not exist"
        }
    }

    # Clean artifacts packages
    if (-not $NoCleanArtifacts) {
        Write-Header "Cleaning artifact packages"
        $artifactsPath = Join-Path $scriptDir "Artifacts/$Configuration"
        if (Test-Path $artifactsPath) {
            $oldArtifacts = Get-ChildItem -Path $artifactsPath -Filter "*.nupkg" -ErrorAction SilentlyContinue
            if ($oldArtifacts) {
                Write-Info "Removing $($oldArtifacts.Count) old package(s) from Artifacts/$Configuration"
                Remove-Item -Path (Join-Path $artifactsPath "*.nupkg") -Force
                Write-Success "Cleaned artifact packages"
            } else {
                Write-Info "No packages to clean in Artifacts/$Configuration"
            }
        } else {
            Write-Info "Artifacts/$Configuration directory does not exist"
        }
    }
}

# Create packages
if (-not $SkipPackage) {
    Write-Header "Creating NuGet packages"

    $packageScript = Join-Path $scriptDir "Create-GranvillePackages.ps1"
    if (-not (Test-Path $packageScript)) {
        Write-Error "Create-GranvillePackages.ps1 not found at $packageScript"
        exit 1
    }

    # Extract suffix without the leading dash for the script
    $suffixOnly = if ($newSuffix) { $newSuffix.TrimStart('-') } else { "" }

    Write-Info "Running Create-GranvillePackages.ps1..."
    & $packageScript -Configuration $Configuration -VersionSuffix $suffixOnly -Mode $Mode

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Package creation failed"
        exit 1
    }
}

Write-Header "Version bump complete!"
Write-Success "Version bumped from $currentVersion$versionSuffix to $fullNewVersion"

if (-not $SkipBuild) {
    Write-Success "Projects built successfully"
}

if (-not $SkipPackage) {
    Write-Success "Packages created successfully"
    Write-Info "Packages are available in: $(Join-Path $scriptDir "../../../Artifacts/$Configuration")"
}

# Provide next steps
Write-Header "Next steps"
Write-Info "1. Clear NuGet caches: dotnet nuget locals all --clear"
Write-Info "2. Test the new packages in sample projects"
Write-Info "3. Commit the version changes"