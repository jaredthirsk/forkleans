#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Development environment manager for Shooter sample with automatic monitoring and restart.

.DESCRIPTION
    Manages the Shooter development environment with:
    - Graceful shutdown of all components
    - Automatic log monitoring for errors
    - Restart on failure
    - Zone transition issue detection

.PARAMETER Action
    start - Start all components
    stop - Gracefully stop all components
    restart - Stop and start all components
    monitor - Monitor logs for issues
    test-loop - Run automated testing loop

.PARAMETER SkipBuild
    Skip building the projects before starting

.PARAMETER WithBot
    Include bot in the startup
#>
param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("start", "stop", "restart", "monitor", "test-loop", "graceful-stop")]
    [string]$Action,
    
    [switch]$SkipBuild = $false,
    [switch]$WithBot = $false,
    [int]$MonitorSeconds = 60
)

$ErrorActionPreference = "Stop"
$script:ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$script:RootDir = Split-Path -Parent $ScriptDir

# Configuration
$script:SiloPort = 7071
$script:ActionServerPorts = @(7072, 7073, 7074, 7075)
$script:ClientPort = 5000
$script:LogDir = Join-Path $RootDir "logs"

function Write-ColoredMessage {
    param(
        [string]$Message,
        [string]$Color = "Green"
    )
    Write-Host $Message -ForegroundColor $Color
}

function Stop-ShooterProcesses {
    Write-ColoredMessage "Stopping all Shooter processes..." "Yellow"
    
    # Use the existing kill script
    $killScript = Join-Path $ScriptDir "kill-shooter-processes.sh"
    if (Test-Path $killScript) {
        & bash $killScript
    } else {
        # Fallback to manual process killing
        Get-Process | Where-Object { $_.ProcessName -match "Shooter\." } | Stop-Process -Force -ErrorAction SilentlyContinue
    }
    
    Start-Sleep -Seconds 2
    Write-ColoredMessage "All processes stopped" "Green"
}

function Send-GracefulShutdown {
    Write-ColoredMessage "Sending graceful shutdown signal to Silo..." "Yellow"
    
    try {
        # Send shutdown request to Silo
        $response = Invoke-WebRequest -Uri "http://localhost:$SiloPort/api/admin/shutdown" `
            -Method POST `
            -TimeoutSec 5 `
            -ErrorAction SilentlyContinue
        
        if ($response.StatusCode -eq 200) {
            Write-ColoredMessage "Graceful shutdown initiated" "Green"
            
            # Wait for processes to terminate gracefully
            $timeout = 10
            $elapsed = 0
            while ($elapsed -lt $timeout) {
                $processes = Get-Process | Where-Object { $_.ProcessName -match "Shooter\." }
                if ($processes.Count -eq 0) {
                    Write-ColoredMessage "All processes terminated gracefully" "Green"
                    return
                }
                Start-Sleep -Seconds 1
                $elapsed++
            }
            
            Write-ColoredMessage "Timeout waiting for graceful shutdown, forcing..." "Yellow"
        }
    }
    catch {
        Write-ColoredMessage "Could not send graceful shutdown signal: $_" "Yellow"
    }
    
    # Force stop if graceful shutdown failed or timed out
    Stop-ShooterProcesses
}

function Start-ShooterComponents {
    Write-ColoredMessage "Starting Shooter components..." "Cyan"
    
    # Ensure log directory exists
    if (-not (Test-Path $LogDir)) {
        New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
    }
    
    # Build if not skipped
    if (-not $SkipBuild) {
        Write-ColoredMessage "Building projects..." "Yellow"
        Push-Location $RootDir
        dotnet build --configuration Debug
        if ($LASTEXITCODE -ne 0) {
            Write-ColoredMessage "Build failed!" "Red"
            Pop-Location
            return $false
        }
        Pop-Location
    }
    
    # Start Silo
    Write-ColoredMessage "Starting Silo..." "Yellow"
    $siloLog = Join-Path $LogDir "silo-dev.log"
    Start-Process -FilePath "dotnet" `
        -ArgumentList "run", "--project", "Shooter.Silo", "--no-build" `
        -WorkingDirectory $RootDir `
        -RedirectStandardOutput $siloLog `
        -RedirectStandardError "$siloLog.err" `
        -NoNewWindow
    
    Start-Sleep -Seconds 5
    
    # Start ActionServers
    foreach ($port in $ActionServerPorts) {
        Write-ColoredMessage "Starting ActionServer on port $port..." "Yellow"
        $actionLog = Join-Path $LogDir "actionserver-$port-dev.log"
        Start-Process -FilePath "dotnet" `
            -ArgumentList "run", "--project", "Shooter.ActionServer", "--no-build", "--", "--urls", "https://localhost:$port" `
            -WorkingDirectory $RootDir `
            -RedirectStandardOutput $actionLog `
            -RedirectStandardError "$actionLog.err" `
            -NoNewWindow
        
        Start-Sleep -Seconds 2
    }
    
    # Start Client
    Write-ColoredMessage "Starting Client..." "Yellow"
    $clientLog = Join-Path $LogDir "client-dev.log"
    Start-Process -FilePath "dotnet" `
        -ArgumentList "run", "--project", "Shooter.Client", "--no-build" `
        -WorkingDirectory $RootDir `
        -RedirectStandardOutput $clientLog `
        -RedirectStandardError "$clientLog.err" `
        -NoNewWindow
    
    # Start Bot if requested
    if ($WithBot) {
        Start-Sleep -Seconds 3
        Write-ColoredMessage "Starting Bot..." "Yellow"
        $botLog = Join-Path $LogDir "bot-dev.log"
        Start-Process -FilePath "dotnet" `
            -ArgumentList "run", "--project", "Shooter.Bot", "--no-build" `
            -WorkingDirectory $RootDir `
            -RedirectStandardOutput $botLog `
            -RedirectStandardError "$botLog.err" `
            -NoNewWindow
    }
    
    Write-ColoredMessage "All components started" "Green"
    return $true
}

function Monitor-Logs {
    param([int]$Seconds = 60)
    
    Write-ColoredMessage "Monitoring logs for $Seconds seconds..." "Cyan"
    
    $endTime = (Get-Date).AddSeconds($Seconds)
    $issuesFound = @()
    
    while ((Get-Date) -lt $endTime) {
        # Check for common issues in logs
        $logFiles = Get-ChildItem -Path $LogDir -Filter "*.log" -ErrorAction SilentlyContinue
        
        foreach ($logFile in $logFiles) {
            $recentLines = Get-Content $logFile -Tail 100 -ErrorAction SilentlyContinue
            
            # Check for timeout errors
            $timeouts = $recentLines | Where-Object { $_ -match "timed out after \d+ms" }
            if ($timeouts) {
                $issuesFound += "TIMEOUT in $($logFile.Name)"
                Write-ColoredMessage "Found timeout issue in $($logFile.Name)" "Red"
            }
            
            # Check for zone mismatch
            $zoneMismatch = $recentLines | Where-Object { $_ -match "ZONE_MISMATCH|zone transition|Transitioning" }
            if ($zoneMismatch) {
                $issuesFound += "ZONE_MISMATCH in $($logFile.Name)"
                Write-ColoredMessage "Found zone mismatch in $($logFile.Name)" "Yellow"
            }
            
            # Check for exceptions
            $exceptions = $recentLines | Where-Object { $_ -match "Exception:|ERROR|FATAL" }
            if ($exceptions) {
                $issuesFound += "EXCEPTION in $($logFile.Name)"
                Write-ColoredMessage "Found exception in $($logFile.Name)" "Red"
            }
        }
        
        if ($issuesFound.Count -gt 0) {
            Write-ColoredMessage "Issues detected: $($issuesFound -join ', ')" "Red"
            return $false
        }
        
        Start-Sleep -Seconds 5
    }
    
    Write-ColoredMessage "No critical issues detected during monitoring" "Green"
    return $true
}

function Start-TestLoop {
    Write-ColoredMessage "Starting automated test loop..." "Cyan"
    
    $iteration = 0
    $maxIterations = 10
    $successCount = 0
    
    while ($iteration -lt $maxIterations) {
        $iteration++
        Write-ColoredMessage "`n=== Test Iteration $iteration/$maxIterations ===" "Cyan"
        
        # Start components
        if (-not (Start-ShooterComponents)) {
            Write-ColoredMessage "Failed to start components" "Red"
            continue
        }
        
        # Monitor for issues
        Start-Sleep -Seconds 10  # Let everything stabilize
        $success = Monitor-Logs -Seconds $MonitorSeconds
        
        if ($success) {
            $successCount++
            Write-ColoredMessage "Iteration $iteration completed successfully" "Green"
        } else {
            Write-ColoredMessage "Iteration $iteration failed with issues" "Red"
        }
        
        # Stop components
        Send-GracefulShutdown
        Start-Sleep -Seconds 5
    }
    
    Write-ColoredMessage "`n=== Test Loop Complete ===" "Cyan"
    Write-ColoredMessage "Success rate: $successCount/$maxIterations ($([int]($successCount * 100 / $maxIterations))%)" "Cyan"
}

# Main execution
switch ($Action) {
    "start" {
        Start-ShooterComponents
    }
    "stop" {
        Stop-ShooterProcesses
    }
    "graceful-stop" {
        Send-GracefulShutdown
    }
    "restart" {
        Send-GracefulShutdown
        Start-Sleep -Seconds 2
        Start-ShooterComponents
    }
    "monitor" {
        Monitor-Logs -Seconds $MonitorSeconds
    }
    "test-loop" {
        Start-TestLoop
    }
}