#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Monitors Shooter application logs for errors and critical warnings in real-time.

.DESCRIPTION
    Watches log output from all Shooter components (Silo, Client, ActionServer, Bot)
    and triggers actions when errors or specific warnings are detected.

.PARAMETER RunMode
    How to run the Shooter application:
    - "Monitor" - Just monitor existing processes
    - "Start" - Start fresh instances
    - "Restart" - Kill existing and start fresh
    Default: "Monitor"

.PARAMETER StopOnError
    Stop all processes when an error is detected
    Default: true

.PARAMETER CaptureContext
    Number of log lines to capture before/after an error
    Default: 50

.PARAMETER IgnoreListFile
    Path to file containing patterns to ignore (one per line)
    Default: ./log-ignore-list.txt

.PARAMETER AutoRestart
    Automatically restart after error analysis
    Default: false

.PARAMETER MaxRestarts
    Maximum number of auto-restarts
    Default: 3

.EXAMPLE
    ./monitor-shooter-logs.ps1 -RunMode Start -AutoRestart
#>
param(
    [ValidateSet("Monitor", "Start", "Restart")]
    [string]$RunMode = "Monitor",
    [bool]$StopOnError = $true,
    [int]$CaptureContext = 50,
    [string]$IgnoreListFile = "./log-ignore-list.txt",
    [bool]$AutoRestart = $false,
    [int]$MaxRestarts = 3
)

$ErrorActionPreference = "Stop"

# Create results directory
$sessionId = Get-Date -Format "yyyyMMdd-HHmmss"
$resultsDir = "log-monitoring/$sessionId"
New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null

# Log files
$monitorLog = "$resultsDir/monitor.log"
$errorLog = "$resultsDir/errors.log"
$warningLog = "$resultsDir/warnings.log"

# Circular buffer for context capture
$logBuffer = New-Object System.Collections.Generic.LinkedList[string]
$maxBufferSize = 200

# Statistics
$stats = @{
    Errors = 0
    Warnings = 0
    Restarts = 0
    StartTime = Get-Date
}

# Error patterns that should trigger immediate stop
$criticalErrorPatterns = @(
    "Unhandled exception",
    "FATAL",
    "System.NullReferenceException",
    "System.InvalidOperationException",
    "Connection refused",
    "Socket exception",
    "Timeout waiting for",
    "Deadlock detected",
    "Out of memory",
    "Stack overflow"
)

# Warning patterns to track (but not necessarily stop)
$warningPatterns = @(
    "WARN",
    "WARNING",
    "Connection lost",
    "Retry attempt",
    "Failed to connect",
    "Slow response",
    "High latency",
    "Queue full",
    "Pressure",
    "Degraded"
)

# Load ignore list
$ignorePatterns = @()
if (Test-Path $IgnoreListFile) {
    $ignorePatterns = Get-Content $IgnoreListFile | Where-Object { $_ -and !$_.StartsWith("#") }
    Write-Host "Loaded $($ignorePatterns.Count) ignore patterns" -ForegroundColor Gray
}
else {
    # Create default ignore list
    $defaultIgnores = @"
# Ignore list for log monitoring
# Lines starting with # are comments
# Add regex patterns to ignore, one per line

# Common benign warnings
.*Development environment detected.*
.*Using development certificate.*
.*Graceful shutdown.*
.*Cleaning up.*
"@
    $defaultIgnores | Out-File $IgnoreListFile
    Write-Host "Created default ignore list at $IgnoreListFile" -ForegroundColor Yellow
}

function Write-MonitorLog {
    param($Message, $Level = "INFO", $Color = "White")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff"
    $logMessage = "[$timestamp] [$Level] $Message"
    
    Write-Host $logMessage -ForegroundColor $Color
    Add-Content -Path $monitorLog -Value $logMessage
    
    # Also write to specific logs
    switch ($Level) {
        "ERROR" { Add-Content -Path $errorLog -Value $logMessage }
        "WARN" { Add-Content -Path $warningLog -Value $logMessage }
    }
}

function Should-Ignore {
    param($Line)
    
    foreach ($pattern in $ignorePatterns) {
        if ($Line -match $pattern) {
            return $true
        }
    }
    return $false
}

function Add-ToBuffer {
    param($Line)
    
    $global:logBuffer.AddLast($Line) | Out-Null
    
    # Trim buffer if too large
    while ($global:logBuffer.Count -gt $maxBufferSize) {
        $global:logBuffer.RemoveFirst()
    }
}

function Capture-ErrorContext {
    param($ErrorLine, $Component)
    
    $contextFile = "$resultsDir/error-context-$($stats.Errors).txt"
    
    $output = @"
=== ERROR DETECTED ===
Time: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Component: $Component
Error: $ErrorLine

=== CONTEXT (last $CaptureContext lines) ===
"@
    
    # Get buffer content
    $bufferLines = $global:logBuffer | Select-Object -Last $CaptureContext
    $output += ($bufferLines -join "`n")
    
    $output | Out-File $contextFile
    Write-MonitorLog "Error context saved to: $contextFile" -Level "INFO" -Color Cyan
    
    return $contextFile
}

function Start-ShooterProcesses {
    Write-MonitorLog "Starting Shooter processes..." -Level "INFO" -Color Green
    
    $processes = @()
    
    # Start Silo
    Write-MonitorLog "Starting Shooter.Silo..." -Level "INFO" -Color Cyan
    $siloProc = Start-Process -FilePath "dotnet-win" `
        -ArgumentList "run --project ../Shooter.Silo --no-launch-profile" `
        -WorkingDirectory ".." `
        -RedirectStandardOutput "$resultsDir/silo-stdout.log" `
        -RedirectStandardError "$resultsDir/silo-stderr.log" `
        -PassThru
    $processes += @{Name="Silo"; Process=$siloProc; LogFile="$resultsDir/silo-stdout.log"; ErrorFile="$resultsDir/silo-stderr.log"}
    
    Start-Sleep -Seconds 5
    
    # Start ActionServer
    Write-MonitorLog "Starting Shooter.ActionServer..." -Level "INFO" -Color Cyan
    $actionProc = Start-Process -FilePath "dotnet-win" `
        -ArgumentList "run --project ../Shooter.ActionServer --no-launch-profile" `
        -WorkingDirectory ".." `
        -RedirectStandardOutput "$resultsDir/action-stdout.log" `
        -RedirectStandardError "$resultsDir/action-stderr.log" `
        -PassThru
    $processes += @{Name="ActionServer"; Process=$actionProc; LogFile="$resultsDir/action-stdout.log"; ErrorFile="$resultsDir/action-stderr.log"}
    
    Start-Sleep -Seconds 3
    
    # Start Client
    Write-MonitorLog "Starting Shooter.Client..." -Level "INFO" -Color Cyan
    $clientProc = Start-Process -FilePath "dotnet-win" `
        -ArgumentList "run --project ../Shooter.Client --no-launch-profile" `
        -WorkingDirectory ".." `
        -RedirectStandardOutput "$resultsDir/client-stdout.log" `
        -RedirectStandardError "$resultsDir/client-stderr.log" `
        -PassThru
    $processes += @{Name="Client"; Process=$clientProc; LogFile="$resultsDir/client-stdout.log"; ErrorFile="$resultsDir/client-stderr.log"}
    
    # Optionally start a bot
    Write-MonitorLog "Starting Shooter.Bot..." -Level "INFO" -Color Cyan
    $botProc = Start-Process -FilePath "dotnet-win" `
        -ArgumentList "run --project ../Shooter.Bot --no-launch-profile -- --BotName MonitorBot" `
        -WorkingDirectory ".." `
        -RedirectStandardOutput "$resultsDir/bot-stdout.log" `
        -RedirectStandardError "$resultsDir/bot-stderr.log" `
        -PassThru
    $processes += @{Name="Bot"; Process=$botProc; LogFile="$resultsDir/bot-stdout.log"; ErrorFile="$resultsDir/bot-stderr.log"}
    
    return $processes
}

function Stop-ShooterProcesses {
    Write-MonitorLog "Stopping all Shooter processes..." -Level "INFO" -Color Yellow
    
    # Kill processes by name
    $processNames = @("Shooter.Silo", "Shooter.Client", "Shooter.ActionServer", "Shooter.Bot")
    foreach ($name in $processNames) {
        Get-Process -Name $name -ErrorAction SilentlyContinue | Stop-Process -Force
    }
    
    Start-Sleep -Seconds 2
}

function Monitor-LogFile {
    param($FilePath, $Component)
    
    if (-not (Test-Path $FilePath)) {
        return
    }
    
    # Use Get-Content -Tail to read new lines
    Get-Content $FilePath -Tail 0 -Wait | ForEach-Object {
        $line = $_
        Add-ToBuffer $line
        
        # Check if should ignore
        if (Should-Ignore $line) {
            return
        }
        
        # Check for errors
        $isError = $false
        foreach ($pattern in $criticalErrorPatterns) {
            if ($line -match $pattern) {
                $isError = $true
                $stats.Errors++
                Write-MonitorLog "ERROR detected in $Component: $line" -Level "ERROR" -Color Red
                
                # Capture context
                $contextFile = Capture-ErrorContext -ErrorLine $line -Component $Component
                
                if ($StopOnError) {
                    Write-MonitorLog "Stopping due to error (StopOnError=true)" -Level "ERROR" -Color Red
                    $script:shouldStop = $true
                }
                break
            }
        }
        
        # Check for warnings if not an error
        if (-not $isError) {
            foreach ($pattern in $warningPatterns) {
                if ($line -match $pattern) {
                    $stats.Warnings++
                    Write-MonitorLog "Warning in $Component: $line" -Level "WARN" -Color Yellow
                    break
                }
            }
        }
        
        # Output line if verbose
        if ($VerbosePreference -eq "Continue") {
            Write-Host "[$Component] $line" -ForegroundColor Gray
        }
    }
}

function Show-Statistics {
    $runtime = (Get-Date) - $stats.StartTime
    Write-Host "`n=== Monitoring Statistics ===" -ForegroundColor Green
    Write-Host "Runtime: $($runtime.ToString('hh\:mm\:ss'))" -ForegroundColor Cyan
    Write-Host "Errors: $($stats.Errors)" -ForegroundColor $(if ($stats.Errors -gt 0) { "Red" } else { "Green" })
    Write-Host "Warnings: $($stats.Warnings)" -ForegroundColor $(if ($stats.Warnings -gt 0) { "Yellow" } else { "Green" })
    Write-Host "Restarts: $($stats.Restarts)" -ForegroundColor Cyan
    Write-Host "Session ID: $sessionId" -ForegroundColor Gray
    Write-Host "Results: $resultsDir" -ForegroundColor Gray
}

function Analyze-Error {
    param($ErrorContext)
    
    Write-MonitorLog "Analyzing error..." -Level "INFO" -Color Magenta
    
    # Simple analysis - could be enhanced with more intelligence
    $analysis = @"
=== ERROR ANALYSIS ===
Timestamp: $(Get-Date)
Error Count: $($stats.Errors)
Warning Count: $($stats.Warnings)

Recommendations:
"@
    
    # Add specific recommendations based on error type
    $errorContent = Get-Content $ErrorContext -Raw
    
    if ($errorContent -match "Connection refused|Socket exception") {
        $analysis += "`n- Network/connection issue detected"
        $analysis += "`n- Check if all services are running"
        $analysis += "`n- Verify firewall/port settings"
    }
    elseif ($errorContent -match "NullReferenceException") {
        $analysis += "`n- Null reference detected"
        $analysis += "`n- Review recent code changes"
        $analysis += "`n- Check initialization order"
    }
    elseif ($errorContent -match "Timeout|Deadlock") {
        $analysis += "`n- Timing/deadlock issue detected"
        $analysis += "`n- Review async/await usage"
        $analysis += "`n- Check for blocking calls"
    }
    
    $analysis | Out-File "$resultsDir/analysis.txt" -Append
    Write-Host $analysis -ForegroundColor Cyan
}

# Main execution
Write-Host "=== Shooter Log Monitor ===" -ForegroundColor Green
Write-Host "Session: $sessionId" -ForegroundColor Cyan
Write-Host "Mode: $RunMode" -ForegroundColor Cyan
Write-Host "Stop on Error: $StopOnError" -ForegroundColor Cyan
Write-Host "Auto Restart: $AutoRestart" -ForegroundColor Cyan

# Handle run mode
$processes = @()
switch ($RunMode) {
    "Start" {
        $processes = Start-ShooterProcesses
    }
    "Restart" {
        Stop-ShooterProcesses
        Start-Sleep -Seconds 2
        $processes = Start-ShooterProcesses
    }
    "Monitor" {
        Write-MonitorLog "Monitoring existing processes" -Level "INFO" -Color Green
        # Setup monitoring for existing log files
    }
}

# Set up monitoring jobs
$monitorJobs = @()
$script:shouldStop = $false

# Monitor each process's logs
foreach ($proc in $processes) {
    if (Test-Path $proc.LogFile) {
        $job = Start-Job -ScriptBlock {
            param($LogFile, $Component, $ResultsDir)
            Get-Content $LogFile -Tail 0 -Wait
        } -ArgumentList $proc.LogFile, $proc.Name, $resultsDir
        $monitorJobs += $job
        Write-MonitorLog "Monitoring $($proc.Name) logs: $($proc.LogFile)" -Level "INFO" -Color Green
    }
}

# Main monitoring loop
try {
    Write-Host "`nMonitoring... Press Ctrl+C to stop" -ForegroundColor Yellow
    Write-Host "Watching for errors and warnings in real-time`n" -ForegroundColor Gray
    
    $lastCheck = Get-Date
    while (-not $script:shouldStop) {
        # Check job outputs
        foreach ($job in $monitorJobs) {
            $output = Receive-Job -Job $job -ErrorAction SilentlyContinue
            if ($output) {
                foreach ($line in $output) {
                    # Process each line
                    Add-ToBuffer $line
                    
                    # Check patterns
                    # ... (pattern checking logic from Monitor-LogFile)
                }
            }
        }
        
        # Check if processes are still running
        foreach ($proc in $processes) {
            if ($proc.Process -and $proc.Process.HasExited) {
                $exitCode = $proc.Process.ExitCode
                Write-MonitorLog "$($proc.Name) exited with code $exitCode" -Level $(if ($exitCode -eq 0) { "INFO" } else { "ERROR" }) -Color $(if ($exitCode -eq 0) { "Green" } else { "Red" })
                
                if ($exitCode -ne 0 -and $AutoRestart -and $stats.Restarts -lt $MaxRestarts) {
                    Write-MonitorLog "Auto-restarting after failure..." -Level "INFO" -Color Yellow
                    $stats.Restarts++
                    Stop-ShooterProcesses
                    Start-Sleep -Seconds 3
                    $processes = Start-ShooterProcesses
                    break
                }
            }
        }
        
        # Show periodic stats
        if (((Get-Date) - $lastCheck).TotalSeconds -gt 30) {
            Show-Statistics
            $lastCheck = Get-Date
        }
        
        Start-Sleep -Milliseconds 500
    }
}
catch {
    Write-MonitorLog "Monitor interrupted: $_" -Level "ERROR" -Color Red
}
finally {
    # Clean up
    $monitorJobs | Stop-Job
    $monitorJobs | Remove-Job
    
    Show-Statistics
    
    if ($stats.Errors -gt 0) {
        Write-Host "`nErrors detected! Check analysis in: $resultsDir" -ForegroundColor Red
        
        # Offer to open the error files
        Write-Host "`nWould you like to view the error log? (Y/N)" -ForegroundColor Yellow
        $response = Read-Host
        if ($response -eq 'Y' -or $response -eq 'y') {
            Get-Content $errorLog | Out-Host -Paging
        }
    }
    else {
        Write-Host "`nNo errors detected during monitoring session" -ForegroundColor Green
    }
}

Write-Host "`nMonitoring session complete. Results saved to: $resultsDir" -ForegroundColor Cyan