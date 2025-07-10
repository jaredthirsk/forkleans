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

# Determine if we're running in WSL2
$isWSL = $false
if (Test-Path "/proc/version") {
    $procVersion = Get-Content "/proc/version" -ErrorAction SilentlyContinue
    if ($procVersion -match "(WSL|Microsoft)") {
        $isWSL = $true
    }
}

# Choose appropriate dotnet command
$dotnetCmd = if ($isWSL) { "dotnet-win" } else { "dotnet" }

# Clean and run
Write-Host "`nCleaning build..." -ForegroundColor Cyan
& $dotnetCmd clean

Write-Host "`nStarting AppHost..." -ForegroundColor Green
Write-Host "Using: $dotnetCmd" -ForegroundColor Gray
& $dotnetCmd run -c Release -- $args