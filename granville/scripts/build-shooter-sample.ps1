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
    Default: true

.PARAMETER RunAfterBuild
    Whether to run the AppHost after building
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
    [bool]$SetupLocalFeed = $true,
    [switch]$RunAfterBuild = $false
)

$ErrorActionPreference = "Stop"

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
        Write-Host "`nCleaning previous builds..." -ForegroundColor Yellow
        dotnet clean GranvilleSamples.sln -c $Configuration -v minimal
        
        # Clear NuGet cache for Granville packages to ensure we get latest
        Write-Host "`nClearing NuGet cache for Granville packages..." -ForegroundColor Yellow
        dotnet nuget locals all --clear | Out-Null
        
        # Restore packages
        Write-Host "`nRestoring packages..." -ForegroundColor Yellow
        dotnet restore GranvilleSamples.sln --verbosity minimal
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Package restore failed"
            exit 1
        }
        
        # Build the solution
        Write-Host "`nBuilding GranvilleSamples.sln..." -ForegroundColor Green
        dotnet build GranvilleSamples.sln -c $Configuration --no-restore --verbosity minimal
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
                dotnet run --no-build -c $Configuration
            }
            finally {
                Pop-Location
            }
        }
        else {
            Write-Host "`nTo run the Shooter sample:" -ForegroundColor Cyan
            Write-Host "  cd granville/samples/Rpc/Shooter.AppHost" -ForegroundColor Gray
            Write-Host "  dotnet run" -ForegroundColor Gray
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    Pop-Location
}