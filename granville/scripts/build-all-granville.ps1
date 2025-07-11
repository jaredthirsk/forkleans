#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds all Granville components in the correct order.

.DESCRIPTION
    This convenience script runs all build steps in the proper sequence:
    -1. Bump Granville revision number (optional, disabled by default)
    0. Clean build artifacts (optional, enabled by default)
    1. Build Granville Orleans assemblies (with BuildAsGranville=true)
    2. Build type-forwarding shims for compatibility
    3. Build Granville RPC packages
    4. Setup local NuGet feed
    5. Build Shooter sample application

.PARAMETER Configuration
    Build configuration (Debug or Release)
    Default: Release

.PARAMETER SkipShims
    Skip building type-forwarding shims
    Default: false

.PARAMETER SkipSample
    Skip building the Shooter sample
    Default: false

.PARAMETER RunSample
    Run the Shooter AppHost after building
    Default: false

.PARAMETER SkipClean
    Skip the cleaning step before building
    Default: false (cleaning is enabled by default)

.PARAMETER CleanArtifacts
    Also clean the Artifacts/Release directory
    Default: false

.PARAMETER BumpRevision
    Bump the Granville revision number before building
    Default: false

.PARAMETER SetupLocalFeed
    Set up the local NuGet feed by copying packages
    Default: false (assumes Artifacts/Release is already in NuGet sources)

.EXAMPLE
    ./build-all-granville.ps1
    Cleans and builds everything in Release mode

.EXAMPLE
    ./build-all-granville.ps1 -RunSample
    Cleans, builds everything and runs the Shooter sample

.EXAMPLE
    ./build-all-granville.ps1 -SkipShims -SkipSample
    Cleans and builds only Granville Orleans assemblies

.EXAMPLE
    ./build-all-granville.ps1 -SkipClean
    Builds without cleaning first

.EXAMPLE
    ./build-all-granville.ps1 -CleanArtifacts
    Cleans everything including Artifacts/Release before building

.EXAMPLE
    ./build-all-granville.ps1 -BumpRevision
    Bumps the revision number and builds everything
#>
param(
    [string]$Configuration = "Release",
    [switch]$SkipShims = $false,
    [switch]$SkipSample = $false,
    [switch]$RunSample = $false,
    [switch]$SkipClean = $false,
    [switch]$CleanArtifacts = $false,
    [switch]$BumpRevision = $false,
    [switch]$SetupLocalFeed = $false
)

$ErrorActionPreference = "Stop"

Write-Host "=== Granville Complete Build ===" -ForegroundColor Green
Write-Host "Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "Clean Before Build: $(-not $SkipClean)" -ForegroundColor Cyan
Write-Host "Bump Revision: $BumpRevision" -ForegroundColor Cyan
Write-Host "Build Shims: $(-not $SkipShims)" -ForegroundColor Cyan
Write-Host "Build Sample: $(-not $SkipSample)" -ForegroundColor Cyan

$startTime = Get-Date

# Step -1: Bump revision if requested
if ($BumpRevision) {
    Write-Host "`n[-1/4] Bumping Granville revision number..." -ForegroundColor Yellow
    
    $bumpScript = Join-Path $PSScriptRoot "bump-granville-version.ps1"
    if (Test-Path $bumpScript) {
        & $bumpScript
        # PowerShell scripts don't always set LASTEXITCODE properly
        # Check if the version was actually updated by reading the file
        # Directory.Build.props is in the repository root, two levels up from scripts folder
        $buildPropsPath = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) "Directory.Build.props"
        $content = Get-Content $buildPropsPath -Raw
        if ($content -match '<GranvilleRevision[^>]*>([^<]+)</GranvilleRevision>') {
            $newRevision = $matches[1]
            Write-Host "  ✓ Bumped to revision: $newRevision" -ForegroundColor Green
        } else {
            Write-Error "Failed to bump Granville revision"
            exit 1
        }
    } else {
        Write-Error "bump-granville-version.ps1 not found"
        exit 1
    }
}
else {
    Write-Host "`n[-1/4] Skipping revision bump (BumpRevision=false)" -ForegroundColor Gray
}

# Step 0: Clean if not skipped
if (-not $SkipClean) {
    Write-Host "`n[0/4] Cleaning build artifacts..." -ForegroundColor Yellow
    
    # Use the consolidated clean script
    $cleanParams = @()
    if ($CleanArtifacts) {
        $cleanParams += "-Artifacts"
    }
    $cleanParams += "-Force"  # Skip confirmation prompt in automated build
    
    & "$PSScriptRoot/clean.ps1" @cleanParams
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Cleaning failed"
        exit 1
    }
}
else {
    Write-Host "`n[0/4] Skipping clean step (SkipClean=true)" -ForegroundColor Gray
}

# Step 1: Build and Package Granville Orleans assemblies
Write-Host "`n[1/4] Building Granville Orleans assemblies..." -ForegroundColor Yellow

# First build the assemblies
$buildScript = if (Test-Path "$PSScriptRoot/build-granville-full.ps1") { 
    "$PSScriptRoot/build-granville-full.ps1" 
} else { 
    "$PSScriptRoot/build-granville-minimal.ps1" 
}
& $buildScript -Configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to build Granville Orleans assemblies"
    exit 1
}

# Then package them
Write-Host "`nPackaging Granville Orleans assemblies..." -ForegroundColor Yellow
$packageScript = "$PSScriptRoot/build-granville-orleans-packages.ps1"
if (Test-Path $packageScript) {
    & $packageScript -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to package Granville Orleans assemblies"
        exit 1
    }
} else {
    Write-Error "build-granville-orleans-packages.ps1 not found"
    exit 1
}

# Step 2: Build type-forwarding shims
if (-not $SkipShims) {
    Write-Host "`n[2/4] Building type-forwarding shims..." -ForegroundColor Yellow
    & "$PSScriptRoot/build-shims.ps1" -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to build type-forwarding shims"
        exit 1
    }
}
else {
    Write-Host "`n[2/4] Skipping type-forwarding shims (SkipShims=true)" -ForegroundColor Gray
}

# Step 3: Build Granville RPC packages
Write-Host "`n[3/5] Building Granville RPC packages..." -ForegroundColor Yellow
$rpcScript = "$PSScriptRoot/build-granville-rpc-packages.ps1"
if (Test-Path $rpcScript) {
    & $rpcScript -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to build Granville RPC packages"
        exit 1
    }
} else {
    Write-Error "build-granville-rpc-packages.ps1 not found"
    exit 1
}

# Step 4: Setup local NuGet feed if requested
if ($SetupLocalFeed) {
    Write-Host "`n[4/5] Setting up local NuGet feed..." -ForegroundColor Yellow
    & "$PSScriptRoot/setup-local-feed.ps1"
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to setup local NuGet feed"
        exit 1
    }
}
else {
    Write-Host "`n[4/5] Skipping local NuGet feed setup (SetupLocalFeed=false)" -ForegroundColor Gray
}

# Step 5: Build Shooter sample
if (-not $SkipSample) {
    Write-Host "`n[5/5] Building Shooter sample..." -ForegroundColor Yellow
    & "$PSScriptRoot/build-shooter-sample.ps1" -Configuration $Configuration -SetupLocalFeed $false -RunAfterBuild:$RunSample
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to build Shooter sample"
        exit 1
    }
}
else {
    Write-Host "`n[5/5] Skipping Shooter sample (SkipSample=true)" -ForegroundColor Gray
}

# Summary
$endTime = Get-Date
$duration = $endTime - $startTime

Write-Host "`n=== Build Summary ===" -ForegroundColor Green
Write-Host "Total build time: $($duration.ToString('mm\:ss'))" -ForegroundColor Cyan
Write-Host "`nPackages created in: Artifacts/Release/" -ForegroundColor Cyan
if (-not $SetupLocalFeed) {
    Write-Host "Add as NuGet source: dotnet nuget add source ./Artifacts/Release --name granville-local" -ForegroundColor Cyan
}

if (-not $SkipSample -and -not $RunSample) {
    Write-Host "`nTo run the Shooter sample:" -ForegroundColor Yellow
    Write-Host "  cd granville/samples/Rpc/Shooter.AppHost" -ForegroundColor Gray
    Write-Host "  dotnet run" -ForegroundColor Gray
}

Write-Host "`n✓ All Granville components built successfully!" -ForegroundColor Green