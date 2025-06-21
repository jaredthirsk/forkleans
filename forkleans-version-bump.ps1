#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Bumps the Forkleans version and optionally builds and packages the RPC libraries.

.DESCRIPTION
    This script automates the process of bumping the Forkleans version number,
    building the projects, and creating NuGet packages for the RPC libraries.

.PARAMETER VersionPart
    Which part of the version to bump: Major, Minor, Patch, or Revision
    Default: Revision

.PARAMETER SpecificVersion
    Set a specific version instead of bumping (e.g., "9.2.0.5-preview3")

.PARAMETER SkipBuild
    Skip building the Forkleans projects
    Default: false

.PARAMETER SkipPackage
    Skip creating NuGet packages
    Default: false

.PARAMETER Configuration
    Build configuration (Debug or Release)
    Default: Release

.EXAMPLE
    ./forkleans-version-bump.ps1
    Bumps the revision number, builds, and packages

.EXAMPLE
    ./forkleans-version-bump.ps1 -VersionPart Patch
    Bumps the patch number

.EXAMPLE
    ./forkleans-version-bump.ps1 -SpecificVersion "9.2.1.0-preview3"
    Sets a specific version

.EXAMPLE
    ./forkleans-version-bump.ps1 -SkipBuild -SkipPackage
    Only bumps the version without building or packaging
#>

[CmdletBinding()]
param(
    [ValidateSet("Major", "Minor", "Patch", "Revision")]
    [string]$VersionPart = "Revision",
    
    [string]$SpecificVersion = "",
    
    [switch]$SkipBuild = $false,
    
    [switch]$SkipPackage = $false,
    
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
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
    
    # Update all Forkleans package versions
    $packagesContent = $packagesContent -replace '(Include="Forkleans\.[^"]+"\s+Version=")[^"]+(")', "`${1}$fullNewVersion`${2}"
    
    Set-Content -Path $rpcPackagesPropsPath -Value $packagesContent -NoNewline
    Write-Success "Updated samples/Rpc/Directory.Packages.props"
}

# Build projects
if (-not $SkipBuild) {
    Write-Header "Building Forkleans projects"
    
    $projectsToBuild = @(
        "src/Orleans.Core.Abstractions/Orleans.Core.Abstractions.csproj",
        "src/Orleans.Core/Orleans.Core.csproj",
        "src/Orleans.Client/Orleans.Client.csproj",
        "src/Rpc/Orleans.Rpc.Abstractions/Orleans.Rpc.Abstractions.csproj",
        "src/Rpc/Orleans.Rpc.Sdk/Orleans.Rpc.Sdk.csproj",
        "src/Rpc/Orleans.Rpc.Client/Orleans.Rpc.Client.csproj",
        "src/Rpc/Orleans.Rpc.Server/Orleans.Rpc.Server.csproj",
        "src/Rpc/Orleans.Rpc.Transport.LiteNetLib/Orleans.Rpc.Transport.LiteNetLib.csproj"
    )
    
    $buildFailed = $false
    foreach ($project in $projectsToBuild) {
        $projectPath = Join-Path $scriptDir $project
        $projectName = Split-Path -Leaf $project
        Write-Info "Building $projectName..."
        
        $buildResult = dotnet build $projectPath -c $Configuration --verbosity quiet
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to build $projectName"
            $buildFailed = $true
        } else {
            Write-Success "Built $projectName"
        }
    }
    
    if ($buildFailed) {
        Write-Error "Some projects failed to build"
        exit 1
    }
}

# Create packages
if (-not $SkipPackage) {
    Write-Header "Creating NuGet packages"
    
    $packageScript = Join-Path $scriptDir "Create-ForkleansPackages.ps1"
    if (-not (Test-Path $packageScript)) {
        Write-Error "Create-ForkleansPackages.ps1 not found at $packageScript"
        exit 1
    }
    
    # Extract suffix without the leading dash for the script
    $suffixOnly = if ($newSuffix) { $newSuffix.TrimStart('-') } else { "" }
    
    Write-Info "Running Create-ForkleansPackages.ps1..."
    & $packageScript -Configuration $Configuration -VersionSuffix $suffixOnly -SkipBuild
    
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
    Write-Info "Packages are available in: $(Join-Path $scriptDir 'local-packages')"
}

# Provide next steps
Write-Header "Next steps"
Write-Info "1. Clear NuGet caches: dotnet nuget locals http-cache --clear"
Write-Info "2. Test the new packages in sample projects"
Write-Info "3. Commit the version changes"