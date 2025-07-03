#!/usr/bin/env pwsh
# Build and package Granville.Rpc packages
param(
    [string]$Configuration = "Release",
    [string]$OutputPath = ".\Artifacts\Release"
)

Write-Host "Building Granville.Rpc packages..." -ForegroundColor Green

# Create output directory
if (!(Test-Path $OutputPath)) {
    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
}

# Clean output directory
Remove-Item "$OutputPath\*" -Force -ErrorAction SilentlyContinue

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

    # Build the project
    dotnet build $project -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed for $project"
        exit 1
    }

    # Pack the project
    dotnet pack $project -c $Configuration -o $OutputPath --no-build
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