#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds and packages Granville.Rpc libraries.

.DESCRIPTION
    This script builds the Granville.Rpc projects and creates NuGet packages.
    It assumes Orleans packages come from official Microsoft.Orleans NuGet packages.

.PARAMETER Configuration
    Build configuration (Debug or Release)
    Default: Release

.PARAMETER OutputPath
    Output directory for NuGet packages
    Default: ./Artifacts/Granville.Rpc

.PARAMETER SkipBuild
    Skip building and only create packages
    Default: false

.PARAMETER Version
    Override the version number (e.g., "9.1.2.51")
    If not specified, uses version from Directory.Build.props

.EXAMPLE
    ./build-granville-rpc.ps1
    Builds and packages all Granville.Rpc projects in Release mode

.EXAMPLE
    ./build-granville-rpc.ps1 -Configuration Debug -Version 9.1.2.51
    Builds Debug packages with specific version
#>
param(
    [string]$Configuration = "Release",
    [string]$OutputPath = "./Artifacts/Granville.Rpc",
    [switch]$SkipBuild = $false,
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

Write-Host "Building Granville.Rpc packages..." -ForegroundColor Green
Write-Host "Configuration: $Configuration" -ForegroundColor Cyan

# Create output directory
if (!(Test-Path $OutputPath)) {
    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
}

# Clean output directory
Write-Host "Cleaning output directory..." -ForegroundColor Yellow
Remove-Item "$OutputPath/*.nupkg" -Force -ErrorAction SilentlyContinue

# RPC projects to build and package
$rpcProjects = @(
    "src/Rpc/Orleans.Rpc.Abstractions/Orleans.Rpc.Abstractions.csproj",
    "src/Rpc/Orleans.Rpc.Client/Orleans.Rpc.Client.csproj",
    "src/Rpc/Orleans.Rpc.Server/Orleans.Rpc.Server.csproj",
    "src/Rpc/Orleans.Rpc.Sdk/Orleans.Rpc.Sdk.csproj",
    "src/Rpc/Orleans.Rpc.Transport.LiteNetLib/Orleans.Rpc.Transport.LiteNetLib.csproj",
    "src/Rpc/Orleans.Rpc.Transport.Ruffles/Orleans.Rpc.Transport.Ruffles.csproj"
)

# Build Orleans-with-Rpc.sln if not skipping build
if (-not $SkipBuild) {
    Write-Host "`nBuilding RPC projects..." -ForegroundColor Green
    
    # First build Orleans.sln to ensure dependencies are built
    Write-Host "Building Orleans core dependencies..." -ForegroundColor Yellow
    $buildArgs = @("build", "Orleans.sln", "-c", $Configuration, "--verbosity", "minimal")
    if ($Version) {
        $buildArgs += "-p:Version=$Version"
    }
    
    & dotnet $buildArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed for Orleans.sln"
        exit 1
    }
    
    # Then build each RPC project
    Write-Host "Building RPC projects..." -ForegroundColor Yellow
    foreach ($project in $rpcProjects) {
        $projectName = [System.IO.Path]::GetFileNameWithoutExtension($project)
        Write-Host "  Building $projectName..." -ForegroundColor Gray
        
        $buildArgs = @("build", $project, "-c", $Configuration, "--verbosity", "minimal")
        if ($Version) {
            $buildArgs += "-p:Version=$Version"
        }
        
        & dotnet $buildArgs
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Build failed for $project"
            exit 1
        }
    }
}

# Pack each RPC project
foreach ($project in $rpcProjects) {
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($project)
    Write-Host "`nPacking $projectName..." -ForegroundColor Cyan
    
    $packArgs = @("pack", $project, "-c", $Configuration, "-o", $OutputPath, "--no-build")
    if ($Version) {
        $packArgs += "-p:Version=$Version"
    }
    
    & dotnet $packArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Pack failed for $project"
        exit 1
    }
}

Write-Host "`nGranville.Rpc packages built successfully!" -ForegroundColor Green
Write-Host "Packages are in: $OutputPath" -ForegroundColor Yellow

# List the packages
Write-Host "`nPackages created:" -ForegroundColor Cyan
Get-ChildItem $OutputPath -Filter "*.nupkg" | ForEach-Object {
    Write-Host "  $($_.Name)" -ForegroundColor Gray
}

# Show how to use these packages
Write-Host "`nTo use these packages locally:" -ForegroundColor Green
Write-Host "1. Add a NuGet source pointing to: $(Resolve-Path $OutputPath)" -ForegroundColor Gray
Write-Host "2. Reference Granville.Rpc.* packages in your project" -ForegroundColor Gray
Write-Host "3. Reference Microsoft.Orleans.* packages (9.1.2) from nuget.org" -ForegroundColor Gray