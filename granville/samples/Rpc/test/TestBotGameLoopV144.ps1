#!/usr/bin/env pwsh

# Comprehensive test for bot game loop with enhanced logging (v144)
Write-Host "=== Testing Bot Game Loop with Enhanced Logging (v144) ===" -ForegroundColor Green

$ErrorActionPreference = "Continue" # Continue on errors to see all logging
Set-Location "C:\forks\orleans\granville\samples\Rpc"

# Cleanup function
function Cleanup-Processes {
    Write-Host "`nCleaning up processes..." -ForegroundColor Yellow
    
    # Kill specific processes
    Get-Process -Name "dotnet" -ErrorAction SilentlyContinue | Where-Object { 
        $_.MainWindowTitle -match "Shooter" -or 
        $_.ProcessName -eq "dotnet" -and $_.StartTime -gt (Get-Date).AddMinutes(-10)
    } | Stop-Process -Force -ErrorAction SilentlyContinue
    
    # Wait for cleanup
    Start-Sleep 2
    Write-Host "Process cleanup completed." -ForegroundColor Gray
}

try {
    # Cleanup any existing processes
    Cleanup-Processes
    
    Write-Host "Step 1: Starting Orleans Silo..." -ForegroundColor Cyan
    $siloJob = Start-Job -ScriptBlock {
        param($workingDir)
        Set-Location $workingDir
        Set-Location "Shooter.Silo"
        dotnet run --urls "http://localhost:7071" 2>&1
    } -ArgumentList (Get-Location)
    
    # Wait for Silo to start
    Start-Sleep 20
    Write-Host "Silo startup phase completed" -ForegroundColor Green
    
    Write-Host "`nStep 2: Starting ActionServer..." -ForegroundColor Cyan
    $actionJob = Start-Job -ScriptBlock {
        param($workingDir)
        Set-Location $workingDir
        Set-Location "Shooter.ActionServer"
        dotnet run --urls "http://localhost:7072" 2>&1
    } -ArgumentList (Get-Location)
    
    # Wait for ActionServer to start
    Start-Sleep 20
    Write-Host "ActionServer startup phase completed" -ForegroundColor Green
    
    # Check if services are responding
    Write-Host "`nStep 3: Verifying services are ready..." -ForegroundColor Cyan
    try {
        $siloTest = Invoke-WebRequest -Uri "http://localhost:7071/health" -TimeoutSec 5 -ErrorAction Stop
        Write-Host "✅ Silo health check: OK" -ForegroundColor Green
    } catch {
        Write-Host "⚠️ Silo health check failed: $($_.Exception.Message)" -ForegroundColor Yellow
    }
    
    try {
        $actionTest = Invoke-WebRequest -Uri "http://localhost:7072/health" -TimeoutSec 5 -ErrorAction Stop
        Write-Host "✅ ActionServer health check: OK" -ForegroundColor Green
    } catch {
        Write-Host "⚠️ ActionServer health check failed: $($_.Exception.Message)" -ForegroundColor Yellow
    }
    
    Write-Host "`nStep 4: Starting Bot with enhanced logging..." -ForegroundColor Cyan
    Set-Location "Shooter.Bot"
    
    # Run bot and capture output
    $botJob = Start-Job -ScriptBlock {
        param($botPath)
        Set-Location $botPath
        dotnet run 2>&1
    } -ArgumentList (Get-Location)
    
    # Monitor bot output for 90 seconds
    $timeout = 90
    $elapsed = 0
    $foundGameLoop = $false
    $foundRpcConnection = $false
    $foundGrainAcquisition = $false
    $foundActions = $false
    
    Write-Host "Monitoring bot output for $timeout seconds..." -ForegroundColor Yellow
    Write-Host "Looking for:" -ForegroundColor Gray
    Write-Host "  🔗 RPC connection establishment" -ForegroundColor Gray
    Write-Host "  🎯 Grain acquisition process" -ForegroundColor Gray  
    Write-Host "  🎮 Game loop entry" -ForegroundColor Gray
    Write-Host "  📤 Action sending" -ForegroundColor Gray
    Write-Host "  📥 World state updates" -ForegroundColor Gray
    Write-Host ""
    
    while ($elapsed -lt $timeout -and $botJob.State -eq "Running") {
        Start-Sleep 3
        $elapsed += 3
        
        # Get bot output
        $output = Receive-Job $botJob -ErrorAction SilentlyContinue
        if ($output) {
            # Process each line of output
            $output -split "`n" | ForEach-Object {
                $line = $_.Trim()
                if (-not [string]::IsNullOrEmpty($line)) {
                    
                    # Highlight key events
                    if ($line -match "RPC client started|beginning grain acquisition") {
                        Write-Host "🔗 $line" -ForegroundColor Blue
                        $foundRpcConnection = $true
                    }
                    elseif ($line -match "Successfully obtained game grain|✅.*grain") {
                        Write-Host "🎯 $line" -ForegroundColor Green
                        $foundGrainAcquisition = $true
                    }
                    elseif ($line -match "entering game loop|🎮") {
                        Write-Host "🎮 $line" -ForegroundColor Magenta
                        $foundGameLoop = $true
                    }
                    elseif ($line -match "Sending actions|📤") {
                        Write-Host "📤 $line" -ForegroundColor Yellow
                        $foundActions = $true
                    }
                    elseif ($line -match "World state updated|📥") {
                        Write-Host "📥 $line" -ForegroundColor Cyan
                    }
                    elseif ($line -match "❌|🚨|Error|Exception|Failed") {
                        Write-Host "❌ $line" -ForegroundColor Red
                    }
                    else {
                        Write-Host "   $line" -ForegroundColor White
                    }
                }
            }
        }
        
        # Show progress
        if (($elapsed % 15) -eq 0) {
            Write-Host "`n⏱️ Elapsed: ${elapsed}s / ${timeout}s" -ForegroundColor Gray
            Write-Host "Status: RPC:$foundRpcConnection | Grain:$foundGrainAcquisition | Loop:$foundGameLoop | Actions:$foundActions" -ForegroundColor Gray
        }
    }
    
    # Final status
    Write-Host "`n=== Final Results ===" -ForegroundColor Green
    Write-Host "🔗 RPC Connection: $(if($foundRpcConnection){'✅ FOUND'}else{'❌ NOT FOUND'})" -ForegroundColor $(if($foundRpcConnection){'Green'}else{'Red'})
    Write-Host "🎯 Grain Acquisition: $(if($foundGrainAcquisition){'✅ FOUND'}else{'❌ NOT FOUND'})" -ForegroundColor $(if($foundGrainAcquisition){'Green'}else{'Red'})
    Write-Host "🎮 Game Loop Entry: $(if($foundGameLoop){'✅ FOUND'}else{'❌ NOT FOUND'})" -ForegroundColor $(if($foundGameLoop){'Green'}else{'Red'})
    Write-Host "📤 Action Sending: $(if($foundActions){'✅ FOUND'}else{'❌ NOT FOUND'})" -ForegroundColor $(if($foundActions){'Green'}else{'Red'})
    
    if ($foundGameLoop -and $foundActions) {
        Write-Host "`n🎉 SUCCESS: Bot reached game loop and is sending actions!" -ForegroundColor Green
        Write-Host "The game loop is functioning properly." -ForegroundColor Green
    }
    elseif ($foundGrainAcquisition) {
        Write-Host "`n⚠️ PARTIAL: Bot connected but didn't reach full game loop" -ForegroundColor Yellow
        Write-Host "RPC connection works but game logic may have issues." -ForegroundColor Yellow
    }
    else {
        Write-Host "`n❌ FAILED: Bot couldn't establish RPC connection" -ForegroundColor Red
        Write-Host "Check if ActionServer is properly running and accessible." -ForegroundColor Red
    }
    
} finally {
    # Stop jobs
    Write-Host "`nStopping bot..." -ForegroundColor Yellow
    if ($botJob) { Stop-Job $botJob -ErrorAction SilentlyContinue; Remove-Job $botJob -ErrorAction SilentlyContinue }
    
    Write-Host "Stopping ActionServer..." -ForegroundColor Yellow  
    if ($actionJob) { Stop-Job $actionJob -ErrorAction SilentlyContinue; Remove-Job $actionJob -ErrorAction SilentlyContinue }
    
    Write-Host "Stopping Silo..." -ForegroundColor Yellow
    if ($siloJob) { Stop-Job $siloJob -ErrorAction SilentlyContinue; Remove-Job $siloJob -ErrorAction SilentlyContinue }
    
    Cleanup-Processes
}

Write-Host "`nBot game loop test completed." -ForegroundColor Green