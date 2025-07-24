#!/usr/bin/env pwsh

# Test bot connection with v143 packages
Write-Host "=== Testing Bot Connection with v143 Packages ===" -ForegroundColor Green

$ErrorActionPreference = "Stop"
Set-Location "C:\forks\orleans\granville\samples\Rpc"

try {
    # Start Orleans Silo
    Write-Host "Starting Orleans Silo..." -ForegroundColor Yellow
    $siloProcess = Start-Process -FilePath "dotnet" -ArgumentList "run", "--project", "Shooter.Silo", "--urls", "http://localhost:7071" -WindowStyle Hidden -PassThru
    Start-Sleep 15
    
    # Start ActionServer  
    Write-Host "Starting ActionServer..." -ForegroundColor Yellow
    $actionProcess = Start-Process -FilePath "dotnet" -ArgumentList "run", "--project", "Shooter.ActionServer", "--urls", "http://localhost:7072" -WindowStyle Hidden -PassThru
    Start-Sleep 15
    
    # Test bot connection
    Write-Host "Testing bot connection..." -ForegroundColor Yellow
    Set-Location "Shooter.Bot"
    
    # Run bot with timeout
    $botJob = Start-Job -ScriptBlock {
        param($botPath)
        Set-Location $botPath
        dotnet run 2>&1
    } -ArgumentList (Get-Location)
    
    # Wait for bot output with timeout
    $timeout = 60
    $elapsed = 0
    $success = $false
    
    while ($elapsed -lt $timeout -and $botJob.State -eq "Running") {
        Start-Sleep 3
        $elapsed += 3
        
        # Check for success indicators in job output
        $output = Receive-Job $botJob -ErrorAction SilentlyContinue
        if ($output) {
            Write-Host "Bot output: $output" -ForegroundColor Cyan
            if ($output -match "Connected to RPC server|Game loop started|Successfully joined|RPC client started|Connected to ActionServer") {
                $success = $true
                break
            }
            if ($output -match "Failed to connect|Connection timeout|Exception|Error:|NU1101") {
                Write-Host "Bot connection failed!" -ForegroundColor Red
                break
            }
        }
    }
    
    # Stop bot
    Stop-Job $botJob
    Remove-Job $botJob
    
    if ($success) {
        Write-Host "✅ Bot connection test PASSED!" -ForegroundColor Green
    } else {
        Write-Host "❌ Bot connection test FAILED or TIMED OUT!" -ForegroundColor Red
    }
    
} finally {
    # Cleanup processes
    Write-Host "Cleaning up processes..." -ForegroundColor Yellow
    if ($siloProcess) { Stop-Process $siloProcess.Id -Force -ErrorAction SilentlyContinue }
    if ($actionProcess) { Stop-Process $actionProcess.Id -Force -ErrorAction SilentlyContinue }
    
    # Kill any remaining dotnet processes
    Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -match "Shooter" } | Stop-Process -Force -ErrorAction SilentlyContinue
}

Write-Host "Bot connection test completed." -ForegroundColor Green