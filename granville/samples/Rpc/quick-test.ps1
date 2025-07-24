#!/usr/bin/env pwsh
# Quick test to generate logs and check for errors

Write-Host "Running quick test to check for serialization errors..." -ForegroundColor Cyan

Set-Location $PSScriptRoot

# Clean up old logs
Write-Host "Cleaning up old logs..." -ForegroundColor Yellow
Remove-Item -Path "logs/*.log" -Force -ErrorAction SilentlyContinue
Remove-Item -Path "Shooter.*/logs/*.log" -Force -ErrorAction SilentlyContinue

# Kill any existing processes
& ./scripts/kill-shooter-processes.sh
Start-Sleep -Seconds 2

# Start Silo with output capture
Write-Host "`nStarting Silo..." -ForegroundColor Green
$siloJob = Start-Job -ScriptBlock {
    Set-Location $using:PSScriptRoot
    & dotnet run --project Shooter.Silo/Shooter.Silo.csproj 2>&1
}

Start-Sleep -Seconds 5

# Start ActionServer with output capture
Write-Host "Starting ActionServer..." -ForegroundColor Green
$actionServerJob = Start-Job -ScriptBlock {
    Set-Location $using:PSScriptRoot
    & dotnet run --project Shooter.ActionServer/Shooter.ActionServer.csproj 2>&1
}

Start-Sleep -Seconds 5

# Start Client briefly to trigger RPC calls
Write-Host "Starting Client to trigger RPC calls..." -ForegroundColor Green
$clientJob = Start-Job -ScriptBlock {
    Set-Location $using:PSScriptRoot
    & dotnet run --project Shooter.Client/Shooter.Client.csproj 2>&1
}

# Let it run for 10 seconds
Start-Sleep -Seconds 10

# Stop all jobs
Write-Host "`nStopping all processes..." -ForegroundColor Yellow
Stop-Job $clientJob -Force
Stop-Job $actionServerJob -Force
Stop-Job $siloJob -Force

# Get job outputs
Write-Host "`n=== CHECKING FOR ERRORS ===" -ForegroundColor Cyan

Write-Host "`nSilo output:" -ForegroundColor Yellow
$siloOutput = Receive-Job $siloJob
$siloOutput | Select-String -Pattern "fail:|error|exception" -Context 2,2 | ForEach-Object { Write-Host $_.Line -ForegroundColor Red }

Write-Host "`nActionServer output:" -ForegroundColor Yellow
$actionServerOutput = Receive-Job $actionServerJob
$actionServerOutput | Select-String -Pattern "fail:|error|exception" -Context 2,2 | ForEach-Object { Write-Host $_.Line -ForegroundColor Red }

Write-Host "`nClient output:" -ForegroundColor Yellow
$clientOutput = Receive-Job $clientJob
$clientOutput | Select-String -Pattern "fail:|error|exception" -Context 2,2 | ForEach-Object { Write-Host $_.Line -ForegroundColor Red }

# Clean up jobs
Remove-Job $siloJob -Force
Remove-Job $actionServerJob -Force
Remove-Job $clientJob -Force

Write-Host "`nTest completed!" -ForegroundColor Green