#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Verifies that upstream Orleans builds correctly without modifications.

.DESCRIPTION
    This script builds Orleans.sln to verify upstream compatibility.
    It builds with the default behavior (BuildAsGranville=false),
    producing Orleans.* assemblies with Orleans.* internal names.

.PARAMETER Configuration
    Build configuration (Debug or Release)
    Default: Release

.PARAMETER Clean
    Clean before building
    Default: true

.EXAMPLE
    ./build-upstream-verification.ps1
    Builds Orleans.sln in Release mode

.EXAMPLE
    ./build-upstream-verification.ps1 -Configuration Debug -Clean $false
    Builds Orleans.sln in Debug mode without cleaning
#>
param(
    [string]$Configuration = "Release",
    [bool]$Clean = $true
)

$ErrorActionPreference = "Stop"

Write-Host "=== Orleans Upstream Verification Build ===" -ForegroundColor Green
Write-Host "Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "BuildAsGranville: false (default)" -ForegroundColor Cyan

# Clean if requested
if ($Clean) {
    Write-Host "`nCleaning previous builds..." -ForegroundColor Yellow
    dotnet clean Orleans.sln -c $Configuration -v minimal
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Clean failed, likely due to missing packages. Clearing NuGet cache and skipping clean..." -ForegroundColor Yellow
        dotnet nuget locals all --clear
        Write-Host "Skipping clean step and proceeding to restore..." -ForegroundColor Yellow
    }
}

# Restore packages explicitly  
Write-Host "`nRestoring packages..." -ForegroundColor Yellow
dotnet restore Orleans.sln --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    Write-Error "Package restore failed for Orleans.sln"
    exit 1
}

# Build Orleans.sln with default behavior (no Granville renaming)
Write-Host "`nBuilding Orleans.sln..." -ForegroundColor Green
$buildArgs = @("build", "Orleans.sln", "-c", $Configuration, "--no-restore", "--verbosity", "minimal")

& dotnet $buildArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed for Orleans.sln"
    exit 1
}

Write-Host "`n✓ Orleans upstream verification build completed successfully!" -ForegroundColor Green
Write-Host "Assemblies are built as Orleans.* with Orleans.* internal names" -ForegroundColor Cyan