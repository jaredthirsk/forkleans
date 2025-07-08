#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds all Granville components in the correct order.

.DESCRIPTION
    This convenience script runs all build steps in the proper sequence:
    1. Build Granville Orleans assemblies (with BuildAsGranville=true)
    2. Build type-forwarding shims for compatibility
    3. Setup local NuGet feed
    4. Build Shooter sample application

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

.EXAMPLE
    ./build-all-granville.ps1
    Builds everything in Release mode

.EXAMPLE
    ./build-all-granville.ps1 -RunSample
    Builds everything and runs the Shooter sample

.EXAMPLE
    ./build-all-granville.ps1 -SkipShims -SkipSample
    Builds only Granville Orleans assemblies
#>
param(
    [string]$Configuration = "Release",
    [switch]$SkipShims = $false,
    [switch]$SkipSample = $false,
    [switch]$RunSample = $false
)

$ErrorActionPreference = "Stop"

Write-Host "=== Granville Complete Build ===" -ForegroundColor Green
Write-Host "Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "Build Shims: $(-not $SkipShims)" -ForegroundColor Cyan
Write-Host "Build Sample: $(-not $SkipSample)" -ForegroundColor Cyan

$startTime = Get-Date

# Step 1: Build Granville Orleans minimal assemblies
Write-Host "`n[1/4] Building Granville Orleans assemblies..." -ForegroundColor Yellow
& "$PSScriptRoot/build-granville-minimal.ps1" -Configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to build Granville Orleans assemblies"
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

# Step 3: Setup local NuGet feed
Write-Host "`n[3/4] Setting up local NuGet feed..." -ForegroundColor Yellow
& "$PSScriptRoot/setup-local-feed.ps1"
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to setup local NuGet feed"
    exit 1
}

# Step 4: Build Shooter sample
if (-not $SkipSample) {
    Write-Host "`n[4/4] Building Shooter sample..." -ForegroundColor Yellow
    & "$PSScriptRoot/build-shooter-sample.ps1" -Configuration $Configuration -SetupLocalFeed $false -RunAfterBuild:$RunSample
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to build Shooter sample"
        exit 1
    }
}
else {
    Write-Host "`n[4/4] Skipping Shooter sample (SkipSample=true)" -ForegroundColor Gray
}

# Summary
$endTime = Get-Date
$duration = $endTime - $startTime

Write-Host "`n=== Build Summary ===" -ForegroundColor Green
Write-Host "Total build time: $($duration.ToString('mm\:ss'))" -ForegroundColor Cyan
Write-Host "`nPackages created in: Artifacts/Release/" -ForegroundColor Cyan
Write-Host "Local NuGet feed: ~/local-nuget-feed/" -ForegroundColor Cyan

if (-not $SkipSample -and -not $RunSample) {
    Write-Host "`nTo run the Shooter sample:" -ForegroundColor Yellow
    Write-Host "  cd granville/samples/Rpc/Shooter.AppHost" -ForegroundColor Gray
    Write-Host "  dotnet run" -ForegroundColor Gray
}

Write-Host "`n✓ All Granville components built successfully!" -ForegroundColor Green