#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds the Shooter sample application using local Granville packages.

.DESCRIPTION
    This script builds the Shooter sample which demonstrates Orleans + RPC + Aspire.
    It uses packages from the local NuGet feed created by other build scripts.

.PARAMETER Configuration
    Build configuration (Debug or Release)
    Default: Release

.PARAMETER SetupLocalFeed
    Whether to set up/update the local NuGet feed
    Default: false (assumes Artifacts/Release is already in NuGet sources)

.PARAMETER RunAfterBuild
    Whether to run the AppHost after building
    Default: false

.PARAMETER SkipClean
    Skip cleaning previous builds
    Default: false

.EXAMPLE
    ./build-shooter-sample.ps1
    Builds the Shooter sample using local packages

.EXAMPLE
    ./build-shooter-sample.ps1 -RunAfterBuild
    Builds and runs the Shooter AppHost
#>
param(
    [string]$Configuration = "Release",
    [bool]$SetupLocalFeed = $false,
    [switch]$RunAfterBuild = $false,
    [switch]$SkipClean = $false
)

$ErrorActionPreference = "Stop"

# Detect if we're running in a container (Docker/devcontainer)
$isContainer = Test-Path "/.dockerenv"

# Determine if we're running in WSL2 (but not in a container)
$isWSL = $false
if (-not $isContainer -and (Test-Path "/proc/version")) {
    $procVersion = Get-Content "/proc/version" -ErrorAction SilentlyContinue
    if ($procVersion -match "(WSL|Microsoft)") {
        $isWSL = $true
    }
}

# Choose appropriate dotnet command
# In containers, always use native dotnet; in WSL2 (not container), use dotnet-win
$dotnetCmd = if ($isContainer) { "dotnet" } elseif ($isWSL) { "dotnet-win" } else { "dotnet" }

# Get repository root
$repoRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName
Push-Location $repoRoot

try {
    Write-Host "=== Shooter Sample Build ===" -ForegroundColor Green
    Write-Host "Configuration: $Configuration" -ForegroundColor Cyan
    
    # Setup local feed if requested
    if ($SetupLocalFeed) {
        Write-Host "`nSetting up local NuGet feed..." -ForegroundColor Yellow
        & "$PSScriptRoot/setup-local-feed.ps1"
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to setup local NuGet feed"
            exit 1
        }
    }
    
    # Navigate to Shooter sample directory
    Push-Location "granville/samples/Rpc"
    
    try {
        # Clean previous builds
        if (-not $SkipClean) {
            Write-Host "`nCleaning previous builds..." -ForegroundColor Yellow
            & $dotnetCmd clean GranvilleSamples.sln -c $Configuration -v minimal
        } else {
            Write-Host "`nSkipping clean step" -ForegroundColor Cyan
        }
        
        # Note: NuGet cache clearing disabled - relying on version bumping instead
        # Write-Host "`nClearing NuGet cache for Granville packages..." -ForegroundColor Yellow
        # dotnet nuget locals all --clear | Out-Null
        
        # Restore packages
        Write-Host "`nRestoring packages..." -ForegroundColor Yellow
        & $dotnetCmd restore GranvilleSamples.sln --verbosity normal
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Package restore failed"
            Write-Host "`nTip: Check if all required packages are available in Artifacts/Release/" -ForegroundColor Yellow
            Write-Host "If not already configured, add this directory as a NuGet source:" -ForegroundColor Yellow
            Write-Host "  $dotnetCmd nuget add source $repoRoot/Artifacts/Release --name granville-local" -ForegroundColor Gray
            Write-Host "You may need to run the build steps individually to diagnose the issue." -ForegroundColor Yellow
            exit 1
        }
        
        # Build the solution
        Write-Host "`nBuilding GranvilleSamples.sln..." -ForegroundColor Green
        & $dotnetCmd build GranvilleSamples.sln -c $Configuration --no-restore --verbosity normal
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Build failed for GranvilleSamples.sln"
            exit 1
        }
        
        Write-Host "`n✓ Shooter sample built successfully!" -ForegroundColor Green
        
        # List built assemblies
        Write-Host "`n=== Built Components ===" -ForegroundColor Green
        $components = @(
            "Shooter.Shared",
            "Shooter.Silo", 
            "Shooter.ActionServer",
            "Shooter.Client",
            "Shooter.Client.Common",
            "Shooter.AppHost"
        )
        
        foreach ($component in $components) {
            $path = "$component/bin/$Configuration"
            if (Test-Path $path) {
                Write-Host "  ✓ $component" -ForegroundColor Gray
            }
        }
        
        # Run if requested
        if ($RunAfterBuild) {
            Write-Host "`n=== Running Shooter AppHost ===" -ForegroundColor Green
            Write-Host "Starting Aspire orchestration..." -ForegroundColor Cyan
            
            Push-Location "Shooter.AppHost"
            try {
                & $dotnetCmd run --no-build -c $Configuration
            }
            finally {
                Pop-Location
            }
        }
        else {
            Write-Host "`nTo run the Shooter sample:" -ForegroundColor Cyan
            Write-Host "  cd granville/samples/Rpc/Shooter.AppHost" -ForegroundColor Gray
            Write-Host "  $dotnetCmd run" -ForegroundColor Gray
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    Pop-Location
}