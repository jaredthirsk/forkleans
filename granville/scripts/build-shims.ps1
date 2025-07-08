#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds type-forwarding shim assemblies for Microsoft.Orleans compatibility.

.DESCRIPTION
    This script generates Microsoft.Orleans.* assemblies that forward types to
    Granville.Orleans.* assemblies. This enables third-party packages that depend
    on Microsoft.Orleans to work with Granville.Orleans implementations.

.PARAMETER Configuration
    Build configuration (Debug or Release)
    Default: Release

.PARAMETER SkipPackaging
    Skip creating NuGet packages
    Default: false

.PARAMETER OutputPath
    Output directory for NuGet packages
    Default: ./Artifacts/Release

.EXAMPLE
    ./build-shims.ps1
    Generates and packages all shim assemblies

.EXAMPLE
    ./build-shims.ps1 -SkipPackaging
    Generates shim assemblies without packaging
#>
param(
    [string]$Configuration = "Release",
    [switch]$SkipPackaging = $false,
    [string]$OutputPath = "./Artifacts/Release"
)

$ErrorActionPreference = "Stop"

# Get repository root
$repoRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName
Push-Location $repoRoot

try {
    Write-Host "=== Type-Forwarding Shims Build ===" -ForegroundColor Green
    Write-Host "Configuration: $Configuration" -ForegroundColor Cyan
    Write-Host "Output: $OutputPath" -ForegroundColor Cyan

    # Check if Granville Orleans assemblies exist
    Write-Host "`nChecking for Granville Orleans assemblies..." -ForegroundColor Yellow
    $requiredAssemblies = @(
        "src/Orleans.Core/bin/$Configuration/net8.0/Granville.Orleans.Core.dll",
        "src/Orleans.Core.Abstractions/bin/$Configuration/net8.0/Granville.Orleans.Core.Abstractions.dll"
    )
    
    $missingAssemblies = @()
    foreach ($assembly in $requiredAssemblies) {
        if (!(Test-Path $assembly)) {
            $missingAssemblies += $assembly
        }
    }
    
    if ($missingAssemblies.Count -gt 0) {
        Write-Error "Missing required Granville assemblies. Please run build-granville-minimal.ps1 first.`nMissing:`n$($missingAssemblies -join "`n")"
        exit 1
    }

    # Change to compatibility tools directory
    Push-Location "granville/compatibility-tools"
    
    try {
        # Generate shim assemblies
        Write-Host "`nGenerating type-forwarding shim assemblies..." -ForegroundColor Green
        & ./generate-individual-shims.ps1
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to generate shim assemblies"
            exit 1
        }
        
        Write-Host "`n✓ Shim assemblies generated successfully!" -ForegroundColor Green
        
        # Package if not skipped
        if (-not $SkipPackaging) {
            Write-Host "`n=== Creating Shim NuGet Packages ===" -ForegroundColor Yellow
            
            & ./package-shims-direct.ps1
            if ($LASTEXITCODE -ne 0) {
                Write-Error "Failed to package shim assemblies"
                exit 1
            }
            
            # List created packages
            Write-Host "`n=== Created Shim Packages ===" -ForegroundColor Green
            Get-ChildItem "$repoRoot/$OutputPath/Microsoft.Orleans.*-granville-shim.nupkg" | Sort-Object Name | ForEach-Object {
                Write-Host "  $($_.Name)" -ForegroundColor Gray
            }
        }
    }
    finally {
        Pop-Location
    }
    
    Write-Host "`n✓ Type-forwarding shims build completed!" -ForegroundColor Green
}
finally {
    Pop-Location
}