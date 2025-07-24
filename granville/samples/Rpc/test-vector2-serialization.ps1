#!/usr/bin/env pwsh
# Test script for Vector2 serialization in RPC

Write-Host "Testing Vector2 serialization in Shooter sample..." -ForegroundColor Cyan

# Kill any existing Shooter processes
Write-Host "Cleaning up existing processes..." -ForegroundColor Yellow
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
Write-Host "Check the console logs for any Vector2 serialization errors" -ForegroundColor Yellow
Write-Host "`nPress any key to stop all processes..." -ForegroundColor Yellow

$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

# Stop all processes
Write-Host "`nStopping all processes..." -ForegroundColor Yellow
$clientProcess.Kill()
$actionServerProcess.Kill()
$siloProcess.Kill()

Write-Host "Test completed!" -ForegroundColor Green