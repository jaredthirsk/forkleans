#!/usr/bin/env pwsh
# Build and package Granville.Rpc packages
param(
    [string]$Configuration = "Release",
    [string]$OutputPath = ".\Artifacts\Release",
    [switch]$SkipClean = $false
)

Write-Host "Building Granville.Rpc packages..." -ForegroundColor Green

# Create output directory
if (!(Test-Path $OutputPath)) {
    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
}

# Clean only Granville.Rpc.* packages from output directory to preserve other packages
if (-not $SkipClean) {
    Write-Host "Cleaning existing Granville.Rpc packages..." -ForegroundColor Yellow
    Remove-Item "$OutputPath\Granville.Rpc.*.nupkg" -Force -ErrorAction SilentlyContinue
} else {
    Write-Host "Skipping cleanup of existing Granville.Rpc packages" -ForegroundColor Cyan
}

# RPC projects to build and package
$rpcProjects = @(
    "src\Rpc\Orleans.Rpc.Abstractions\Orleans.Rpc.Abstractions.csproj",
    "src\Rpc\Orleans.Rpc.Client\Orleans.Rpc.Client.csproj",
    "src\Rpc\Orleans.Rpc.Server\Orleans.Rpc.Server.csproj",
    "src\Rpc\Orleans.Rpc.Sdk\Orleans.Rpc.Sdk.csproj",
    "src\Rpc\Orleans.Rpc.Transport.LiteNetLib\Orleans.Rpc.Transport.LiteNetLib.csproj",
    "src\Rpc\Orleans.Rpc.Transport.Ruffles\Orleans.Rpc.Transport.Ruffles.csproj"
)

# Build and pack each project
foreach ($project in $rpcProjects) {
    Write-Host "`nBuilding and packing $project..." -ForegroundColor Cyan

    # Build the project with BuildAsGranville to ensure Orleans dependencies get -granville-shim suffix
    dotnet build $project -c $Configuration -p:BuildAsGranville=true
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed for $project"
        exit 1
    }

    # Pack the project with BuildAsGranville to ensure Orleans dependencies get -granville-shim suffix
    dotnet pack $project -c $Configuration -o $OutputPath --no-build -p:BuildAsGranville=true
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