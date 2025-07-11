#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds the minimal set of Orleans and RPC projects needed for Granville.

.DESCRIPTION
    This script builds Granville.Minimal.sln with BuildAsGranville=true,
    producing Granville.Orleans.* assemblies with Granville.Orleans.* internal names.
    It then packages all assemblies into NuGet packages.

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
    ./build-granville-minimal.ps1
    Builds and packages all Granville assemblies

.EXAMPLE
    ./build-granville-minimal.ps1 -SkipPackaging
    Builds assemblies only without packaging
#>
param(
    [string]$Configuration = "Release",
    [switch]$SkipPackaging = $false,
    [string]$OutputPath = "./Artifacts/Release"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Granville Minimal Build ===" -ForegroundColor Green
Write-Host "Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "BuildAsGranville: true" -ForegroundColor Cyan
Write-Host "Output: $OutputPath" -ForegroundColor Cyan

# Create output directory
if (!(Test-Path $OutputPath)) {
    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
}

# Clean previous builds
Write-Host "`nCleaning previous builds..." -ForegroundColor Yellow
dotnet clean Granville.Minimal.sln -c $Configuration -v minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host "Clean failed, likely due to missing packages. Skipping clean..." -ForegroundColor Yellow
    # Note: NuGet cache clearing disabled - relying on version bumping instead
    # dotnet nuget locals all --clear
}

# Restore packages explicitly
Write-Host "`nRestoring packages..." -ForegroundColor Yellow
dotnet restore Granville.Minimal.sln --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    Write-Error "Package restore failed for Granville.Minimal.sln"
    exit 1
}

# Build Granville.Minimal.sln with Granville naming
Write-Host "`nBuilding Granville.Minimal.sln..." -ForegroundColor Green
$buildArgs = @("build", "Granville.Minimal.sln", "-c", $Configuration, "-p:BuildAsGranville=true", "--no-restore", "--verbosity", "minimal")

& dotnet $buildArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed for Granville.Minimal.sln"
    exit 1
}

Write-Host "`n✓ Build completed successfully!" -ForegroundColor Green

# Package if not skipped
if (-not $SkipPackaging) {
    Write-Host "`n=== Creating NuGet Packages ===" -ForegroundColor Yellow
    
    # Orleans packages to create
    $orleansProjects = @(
        "src/Orleans.Core.Abstractions/Orleans.Core.Abstractions.csproj",
        "src/Orleans.Core/Orleans.Core.csproj",
        "src/Orleans.Serialization.Abstractions/Orleans.Serialization.Abstractions.csproj",
        "src/Orleans.Serialization/Orleans.Serialization.csproj",
        "src/Orleans.CodeGenerator/Orleans.CodeGenerator.csproj",
        "src/Orleans.Analyzers/Orleans.Analyzers.csproj",
        "src/Orleans.Runtime/Orleans.Runtime.csproj",
        "src/Orleans.Sdk/Orleans.Sdk.csproj"
    )
    
    # RPC packages to create
    $rpcProjects = @(
        "src/Rpc/Orleans.Rpc.Abstractions/Orleans.Rpc.Abstractions.csproj",
        "src/Rpc/Orleans.Rpc.Client/Orleans.Rpc.Client.csproj",
        "src/Rpc/Orleans.Rpc.Server/Orleans.Rpc.Server.csproj",
        "src/Rpc/Orleans.Rpc.Sdk/Orleans.Rpc.Sdk.csproj",
        "src/Rpc/Orleans.Rpc.Transport.LiteNetLib/Orleans.Rpc.Transport.LiteNetLib.csproj",
        "src/Rpc/Orleans.Rpc.Transport.Ruffles/Orleans.Rpc.Transport.Ruffles.csproj"
    )
    
    Write-Host "`nPackaging Orleans assemblies..." -ForegroundColor Cyan
    foreach ($project in $orleansProjects) {
        $projectName = [System.IO.Path]::GetFileNameWithoutExtension($project)
        Write-Host "  Packaging $projectName..." -ForegroundColor Gray
        
        $packArgs = @("pack", $project, "-c", $Configuration, "-p:BuildAsGranville=true", 
                      "--no-build", "-o", $OutputPath, "--verbosity", "minimal")
        
        & dotnet $packArgs
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Failed to pack $projectName"
        }
    }
    
    Write-Host "`nPackaging RPC assemblies..." -ForegroundColor Cyan
    foreach ($project in $rpcProjects) {
        $projectName = [System.IO.Path]::GetFileNameWithoutExtension($project)
        Write-Host "  Packaging $projectName..." -ForegroundColor Gray
        
        $packArgs = @("pack", $project, "-c", $Configuration, 
                      "--no-build", "-o", $OutputPath, "--verbosity", "minimal")
        
        & dotnet $packArgs
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Failed to pack $projectName"
        }
    }
    
    # List created packages
    Write-Host "`n=== Created Packages ===" -ForegroundColor Green
    Get-ChildItem "$OutputPath/*.nupkg" | Sort-Object Name | ForEach-Object {
        Write-Host "  $($_.Name)" -ForegroundColor Gray
    }
}

Write-Host "`n✓ Granville minimal build completed!" -ForegroundColor Green