#!/usr/bin/env pwsh

# Windows version of rw.sh
Write-Host "Cleaning up processes and logs..." -ForegroundColor Cyan

# Kill existing processes
Get-Process | Where-Object { $_.ProcessName -match "Shooter\.(Silo|ActionServer|Client|Bot|AppHost)" } | ForEach-Object {
    Write-Host "Stopping $($_.ProcessName)..." -ForegroundColor Yellow
    Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
}

# Clean logs
Remove-Item ../logs/*.log -Force -ErrorAction SilentlyContinue

# Clean and run
Write-Host "`nCleaning build..." -ForegroundColor Cyan
dotnet clean

Write-Host "`nStarting AppHost..." -ForegroundColor Green
dotnet run -c Release -- $args