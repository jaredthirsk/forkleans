#!/usr/bin/env pwsh
# Workaround script to build Shooter sample with Orleans 9.1.2 code generation issues

Write-Host "Building Shooter sample with workaround for Orleans 9.1.2 code generation issues..." -ForegroundColor Yellow

# Clean Shooter.Shared to prevent duplicate generation
Write-Host "Cleaning Shooter.Shared..." -ForegroundColor Cyan
Remove-Item -Path "Shooter.Shared/obj" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path "Shooter.Shared/bin" -Recurse -Force -ErrorAction SilentlyContinue

# Use pre-built Shooter.Shared.dll if available
$preBuiltDll = "Shooter.Silo/bin/Release/net9.0/Shooter.Shared.dll"
if (Test-Path $preBuiltDll) {
    Write-Host "Using pre-built Shooter.Shared.dll..." -ForegroundColor Green
    New-Item -ItemType Directory -Path "Shooter.Shared/bin/Release/net9.0" -Force | Out-Null
    Copy-Item "$preBuiltDll" "Shooter.Shared/bin/Release/net9.0/" -Force
    Copy-Item "Shooter.Silo/bin/Release/net9.0/Shooter.Shared.*" "Shooter.Shared/bin/Release/net9.0/" -Force
    
    # Touch the DLL to update timestamp
    (Get-Item "Shooter.Shared/bin/Release/net9.0/Shooter.Shared.dll").LastWriteTime = Get-Date
} else {
    Write-Host "Pre-built Shooter.Shared.dll not found. Build may fail due to Orleans code generation issues." -ForegroundColor Red
}

# Build other projects
Write-Host "Building Shooter.Silo..." -ForegroundColor Cyan
dotnet build Shooter.Silo/Shooter.Silo.csproj -c Release

Write-Host "Building Shooter.ActionServer..." -ForegroundColor Cyan
dotnet build Shooter.ActionServer/Shooter.ActionServer.csproj -c Release

Write-Host "Building Shooter.Client..." -ForegroundColor Cyan
dotnet build Shooter.Client/Shooter.Client.csproj -c Release

Write-Host "Building Shooter.AppHost..." -ForegroundColor Cyan
dotnet build Shooter.AppHost/Shooter.AppHost.csproj -c Release

Write-Host "Build complete!" -ForegroundColor Green
Write-Host "Note: This is a workaround for Orleans 9.1.2 code generation issues." -ForegroundColor Yellow
Write-Host "To run: cd Shooter.AppHost && dotnet run" -ForegroundColor Yellow