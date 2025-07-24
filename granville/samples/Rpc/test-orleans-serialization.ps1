#!/usr/bin/env pwsh
# Test script for Orleans serialization of custom types in RPC

Write-Host "Testing Orleans serialization for custom types in RPC..." -ForegroundColor Cyan

# Rebuild the Shooter sample to ensure it uses the latest RPC libraries
Write-Host "`nRebuilding Shooter sample..." -ForegroundColor Yellow
Set-Location $PSScriptRoot

dotnet-win build Shooter.Shared/Shooter.Shared.csproj -c Release
dotnet-win build Shooter.ActionServer/Shooter.ActionServer.csproj -c Release  
dotnet-win build Shooter.Client/Shooter.Client.csproj -c Release
dotnet-win build Shooter.Silo/Shooter.Silo.csproj -c Release

Write-Host "`nBuild completed!" -ForegroundColor Green

# Kill any existing Shooter processes
Write-Host "`nCleaning up existing processes..." -ForegroundColor Yellow
& ./scripts/kill-shooter-processes.sh

Start-Sleep -Seconds 2

# Start the Silo
Write-Host "Starting Silo..." -ForegroundColor Green
$siloProcess = Start-Process -FilePath "dotnet" -ArgumentList "run", "--project", "Shooter.Silo/Shooter.Silo.csproj" -PassThru -NoNewWindow

Start-Sleep -Seconds 5

# Start the ActionServer
Write-Host "Starting ActionServer..." -ForegroundColor Green
$actionServerProcess = Start-Process -FilePath "dotnet" -ArgumentList "run", "--project", "Shooter.ActionServer/Shooter.ActionServer.csproj" -PassThru -NoNewWindow

Start-Sleep -Seconds 5

# Start the Client
Write-Host "Starting Client..." -ForegroundColor Green
$clientProcess = Start-Process -FilePath "dotnet" -ArgumentList "run", "--project", "Shooter.Client/Shooter.Client.csproj" -PassThru -NoNewWindow

Write-Host "`nAll components started!" -ForegroundColor Cyan
Write-Host "Open your browser to http://localhost:5000 to test the game" -ForegroundColor Cyan
Write-Host "Use WASD keys to move and SPACE to shoot" -ForegroundColor Cyan
Write-Host "`nThe RPC framework now uses Orleans serialization for ALL types." -ForegroundColor Green
Write-Host "This means any type with [GenerateSerializer] will work automatically!" -ForegroundColor Green
Write-Host "`nPress any key to stop all processes..." -ForegroundColor Yellow

$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

# Stop all processes
Write-Host "`nStopping all processes..." -ForegroundColor Yellow
$clientProcess.Kill()
$actionServerProcess.Kill()
$siloProcess.Kill()

Write-Host "Test completed!" -ForegroundColor Green