#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Enhanced AI-driven development loop with comprehensive log monitoring.

.DESCRIPTION
    This enhanced version monitors multiple log sources in real-time:
    - File logs from all components
    - Aspire dashboard logs (if available)
    - Process health
    - Real-time pattern matching for critical errors

.PARAMETER MaxIterations
    Maximum number of fix attempts
    Default: 10

.PARAMETER RunDuration
    How long to run before considering it stable (seconds)
    Default: 600 (10 minutes)

.PARAMETER AutoFix
    Whether to automatically attempt fixes (requires AI interaction)
    Default: true

.PARAMETER LogCheckInterval
    How often to check logs in milliseconds
    Default: 500
#>
param(
    [int]$MaxIterations = 10,
    [int]$RunDuration = 600,
    [bool]$AutoFix = $true,
    [int]$LogCheckInterval = 500
)

$ErrorActionPreference = "Stop"

# Session setup
$sessionId = Get-Date -Format "yyyyMMdd-HHmmss"
$workDir = "ai-dev-loop/$sessionId"
New-Item -ItemType Directory -Force -Path $workDir | Out-Null

# State file for AI to read
$stateFile = "$workDir/current-state.json"
$errorFile = "$workDir/last-error.txt"
$contextFile = "$workDir/error-context.log"
$instructionsFile = "$workDir/ai-instructions.txt"

# Statistics
$stats = @{
    Iteration = 0
    ErrorsFound = 0
    FixesApplied = 0
    SuccessfulRuns = 0
    StartTime = Get-Date
    LastError = $null
    State = "Starting"
}

# Enhanced error patterns with severity levels
$errorPatterns = @{
    Critical = @(
        # Zone transition deadlocks
        @{ Pattern = 'PROLONGED_MISMATCH.*for (\d{4,})\.\d+ms'; Description = 'Zone mismatch >4 seconds'; Regex = $true },
        @{ Pattern = 'CHRONIC_MISMATCH'; Description = 'Repeated zone mismatches'; Regex = $false },
        @{ Pattern = 'Player in zone \([^)]+\) but connected to server for zone \([^)]+\) for \d{4,}'; Description = 'Zone sync failure'; Regex = $true },

        # Connection failures
        @{ Pattern = 'Bot.*failed to connect to game'; Description = 'Bot connection failure'; Regex = $true },
        @{ Pattern = 'SignalR connection closed'; Description = 'SignalR disconnection'; Regex = $false },
        @{ Pattern = 'SSL connection could not be established'; Description = 'SSL certificate issue'; Regex = $false },

        # RPC failures
        @{ Pattern = 'Player input RPC failed'; Description = 'RPC failure'; Regex = $false },
        @{ Pattern = 'Error registering player'; Description = 'Registration failure'; Regex = $false },

        # Hangs and timeouts
        @{ Pattern = 'Operation timed out after (\d{5,})ms'; Description = 'Operation timeout >10s'; Regex = $true },
        @{ Pattern = 'Request timeout.*exceeded \d{5,}ms'; Description = 'Request timeout >10s'; Regex = $true },
        @{ Pattern = 'No response for \d{5,}ms'; Description = 'Unresponsive for >10s'; Regex = $true },
        @{ Pattern = 'Deadlock detected'; Description = 'Deadlock condition'; Regex = $false },
        @{ Pattern = 'Thread pool starvation'; Description = 'Thread pool exhausted'; Regex = $false },

        # Client heartbeat monitoring
        @{ Pattern = '\[HEARTBEAT\] CRITICAL:.*unresponsive for (\d+)'; Description = 'Client hang detected'; Regex = $true },
        @{ Pattern = '\[HEARTBEAT\] WARNING:.*hanging'; Description = 'Client hang warning'; Regex = $false },
        @{ Pattern = 'Client has been unresponsive'; Description = 'Client unresponsive'; Regex = $false }
    )

    Severe = @(
        @{ Pattern = '\[Error\]'; Description = 'General error'; Regex = $false },
        # Match "Exception:" but not "Exception: None" (which indicates no exception)
        @{ Pattern = 'Exception:(?! None)'; Description = 'Exception occurred'; Regex = $true },
        @{ Pattern = '\[HEALTH_MONITOR\].*ERROR'; Description = 'Health monitor error'; Regex = $true }
    )

    Warning = @(
        @{ Pattern = 'Retry attempt \d+'; Description = 'Retry occurring'; Regex = $true },
        @{ Pattern = 'High latency detected'; Description = 'Performance issue'; Regex = $false }
    )
}

# Track file positions for incremental reading
$logFilePositions = @{}

# Track last activity times for hang detection
$lastActivityTimes = @{}

function Write-State {
    param($State, $Message = "")

    $stats.State = $State
    $stats.LastUpdate = Get-Date
    $stats.Message = $Message

    $stats | ConvertTo-Json -Depth 3 | Out-File $stateFile

    $color = switch($State) {
        "Running" { "Green" }
        "Error" { "Red" }
        "Investigating" { "Yellow" }
        "Fixing" { "Cyan" }
        "Success" { "Green" }
        default { "White" }
    }

    Write-Host "[$State] $Message" -ForegroundColor $color
}

function Get-LogFiles {
    # Get all current log files
    $logPaths = @(
        "$PSScriptRoot/../logs/*.log",
        "$PSScriptRoot/../logs/*-console.log",
        "$workDir/*.log",
        "$PSScriptRoot/../ai-dev-loop/*/client*.log",
        "$PSScriptRoot/../ai-dev-loop/*/bot*.log",
        "$PSScriptRoot/../ai-dev-loop/*/silo*.log",
        "$PSScriptRoot/../ai-dev-loop/*/action*.log"
    )

    $files = @()
    foreach ($path in $logPaths) {
        $resolved = Get-Item $path -ErrorAction SilentlyContinue
        if ($resolved) {
            $files += $resolved
        }
    }

    return $files | Select-Object -Unique
}

function Read-IncrementalLog {
    param($FilePath)

    if (-not (Test-Path $FilePath)) {
        return @()
    }

    $fileInfo = Get-Item $FilePath
    $currentLength = $fileInfo.Length

    # Initialize position if not tracked
    if (-not $logFilePositions.ContainsKey($FilePath)) {
        # Start from near the end for existing files
        $logFilePositions[$FilePath] = [Math]::Max(0, $currentLength - 10000)
    }

    $lastPosition = $logFilePositions[$FilePath]

    if ($currentLength -le $lastPosition) {
        return @()
    }

    # Read new content
    $stream = [System.IO.FileStream]::new($FilePath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    $reader = [System.IO.StreamReader]::new($stream)

    $stream.Seek($lastPosition, [System.IO.SeekOrigin]::Begin) | Out-Null
    $newContent = $reader.ReadToEnd()

    $reader.Close()
    $stream.Close()

    # Update position
    $logFilePositions[$FilePath] = $currentLength

    # Return lines
    return $newContent -split "`n" | Where-Object { $_ -ne "" }
}

function Check-AspireDashboard {
    # Try to get logs from Aspire dashboard
    try {
        # Aspire dashboard on port 15033
        $response = Invoke-WebRequest -Uri "http://localhost:15033" -TimeoutSec 2 -ErrorAction SilentlyContinue
        if ($response.StatusCode -eq 200) {
            # Dashboard is up but we need to find the actual API endpoint
            # For now, just note that dashboard is accessible
            return $true
        }
    }
    catch {
        # Dashboard not accessible or no API
    }
    return $false
}

function Monitor-ForErrors {
    param($Processes, $Duration)

    $startTime = Get-Date
    $endTime = $startTime.AddSeconds($Duration)
    $errorsFound = @()
    $criticalErrorFound = $false

    Write-Host "Enhanced monitoring for $Duration seconds..." -ForegroundColor Cyan
    Write-Host "Checking logs every $LogCheckInterval ms" -ForegroundColor Gray
    Write-Host "Monitoring patterns: Zone mismatches, RPC failures, Connection issues" -ForegroundColor Gray

    # Check if Aspire dashboard is available
    if (Check-AspireDashboard) {
        Write-Host "Aspire dashboard detected on port 15033" -ForegroundColor Green
    }

    # Progress tracking
    $lastProgressUpdate = Get-Date
    $dotsCount = 0

    while ((Get-Date) -lt $endTime) {
        # Visual progress indicator
        if ((Get-Date).Subtract($lastProgressUpdate).TotalSeconds -ge 1) {
            $elapsed = [int]((Get-Date).Subtract($startTime).TotalSeconds)
            $remaining = [int]($Duration - $elapsed)
            Write-Host "`r[$elapsed/$Duration s] Monitoring" -NoNewline -ForegroundColor DarkGray
            Write-Host ("." * (++$dotsCount % 10)) -NoNewline -ForegroundColor DarkGray
            Write-Host (" " * 10) -NoNewline  # Clear trailing dots
            $lastProgressUpdate = Get-Date
        }

        # Check process health
        foreach ($proc in $Processes) {
            if ($proc.Process.HasExited) {
                $exitCode = $proc.Process.ExitCode
                if ($exitCode -ne 0) {
                    $criticalErrorFound = $true
                    $errorsFound += @{
                        Severity = "Critical"
                        Time = Get-Date
                        Message = "Process crashed: $($proc.Name) with exit code $exitCode"
                        Source = "Process Monitor"
                    }
                }
            }
        }

        # Check all log files incrementally
        $logFiles = Get-LogFiles
        foreach ($logFile in $logFiles) {
            $newLines = Read-IncrementalLog -FilePath $logFile.FullName

            # Track activity for hang detection
            if ($newLines.Count -gt 0) {
                $lastActivityTimes[$logFile.FullName] = Get-Date
            }
            elseif ($lastActivityTimes.ContainsKey($logFile.FullName)) {
                # Only check primary log files (not stderr logs which are often empty)
                # Increased threshold to 120 seconds to reduce false positives
                $isPrimaryLog = $logFile.Name -notmatch '-err\.log$'
                $timeSinceLastActivity = (Get-Date) - $lastActivityTimes[$logFile.FullName]
                if ($isPrimaryLog -and $timeSinceLastActivity.TotalSeconds -gt 120) {
                    $errorsFound += @{
                        Severity = "Warning"  # Downgraded from Critical
                        Time = Get-Date
                        Message = "Potential hang detected: No logs from $($logFile.Name) for $([int]$timeSinceLastActivity.TotalSeconds) seconds"
                        Pattern = "Log activity stopped"
                        Source = $logFile.Name
                    }
                    # Reset to avoid repeated detection
                    $lastActivityTimes[$logFile.FullName] = Get-Date
                }
            }

            foreach ($line in $newLines) {
                # Skip empty lines
                if ([string]::IsNullOrWhiteSpace($line)) { continue }

                # Check each pattern category
                foreach ($severity in @("Critical", "Severe", "Warning")) {
                    foreach ($pattern in $errorPatterns[$severity]) {
                        $matched = $false

                        if ($pattern.Regex) {
                            if ($line -match $pattern.Pattern) {
                                $matched = $true
                            }
                        }
                        else {
                            if ($line -like "*$($pattern.Pattern)*") {
                                $matched = $true
                            }
                        }

                        if ($matched) {
                            $errorEntry = @{
                                Severity = $severity
                                Time = Get-Date
                                Message = $line
                                Pattern = $pattern.Description
                                Source = $logFile.Name
                            }

                            $errorsFound += $errorEntry

                            # Show critical errors immediately
                            if ($severity -eq "Critical") {
                                Write-Host "`n[CRITICAL] $($pattern.Description): $line" -ForegroundColor Red
                                $criticalErrorFound = $true
                            }
                        }
                    }
                }
            }
        }

        # Stop immediately on critical errors
        if ($criticalErrorFound -and $AutoFix) {
            Write-Host "`nCritical error detected - stopping monitoring" -ForegroundColor Red
            break
        }

        Start-Sleep -Milliseconds $LogCheckInterval
    }

    Write-Host "" # New line after progress

    # Process results
    if ($errorsFound.Count -gt 0) {
        # Group errors by severity
        $criticalErrors = $errorsFound | Where-Object { $_.Severity -eq "Critical" }
        $severeErrors = $errorsFound | Where-Object { $_.Severity -eq "Severe" }
        $warnings = $errorsFound | Where-Object { $_.Severity -eq "Warning" }

        # Generate detailed report
        $errorReport = @"
=== ENHANCED ERROR DETECTION REPORT ===
Timestamp: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
Monitoring Duration: $Duration seconds
Total Issues Found: $($errorsFound.Count)

CRITICAL ERRORS ($($criticalErrors.Count)):
$($criticalErrors | ForEach-Object { "[$($_.Time.ToString('HH:mm:ss'))] $($_.Pattern) - Source: $($_.Source)`n  $($_.Message)" } | Out-String)

SEVERE ERRORS ($($severeErrors.Count)):
$($severeErrors | Select-Object -First 10 | ForEach-Object { "[$($_.Time.ToString('HH:mm:ss'))] $($_.Pattern) - Source: $($_.Source)" } | Out-String)

WARNINGS ($($warnings.Count)):
$($warnings | Select-Object -First 5 | ForEach-Object { "[$($_.Time.ToString('HH:mm:ss'))] $($_.Pattern)" } | Out-String)

ANALYSIS:
$(
    if ($criticalErrors | Where-Object { $_.Pattern -like "*Zone*" }) {
        "- Zone transition deadlock detected - players stuck between zones"
    }
    if ($criticalErrors | Where-Object { $_.Pattern -like "*RPC*" }) {
        "- RPC communication failures affecting gameplay"
    }
    if ($criticalErrors | Where-Object { $_.Pattern -like "*SSL*" -or $_.Pattern -like "*SignalR*" }) {
        "- Connection/certificate issues preventing proper communication"
    }
)

RECOMMENDED ACTIONS:
1. For zone issues: Check zone transition logic and force reconnection
2. For RPC failures: Verify network configuration and retry logic
3. For SSL issues: Check certificate trust and SignalR endpoints
4. For bot issues: Verify connection URLs and authentication
"@

        $errorReport | Out-File $errorFile

        # Also save full context
        $contextLines = @()
        foreach ($error in $errorsFound | Select-Object -First 50) {
            $contextLines += "[$($error.Time.ToString('HH:mm:ss.fff'))] [$($error.Severity)] [$($error.Source)]"
            $contextLines += "  Pattern: $($error.Pattern)"
            $contextLines += "  Message: $($error.Message)"
            $contextLines += ""
        }
        $contextLines | Out-String | Out-File $contextFile

        $stats.ErrorsFound += $errorsFound.Count
        $stats.LastError = "$($criticalErrors.Count) critical, $($severeErrors.Count) severe, $($warnings.Count) warnings"

        Write-State "Error" "Detected: $($stats.LastError)"
        return $false
    }

    $stats.SuccessfulRuns++
    Write-State "Success" "No errors detected during $Duration second monitoring"
    return $true
}

function Start-ShooterWithLogging {
    Write-State "Starting" "Launching Shooter via rl.sh..."

    # Kill any existing processes first
    & "$PSScriptRoot/../scripts/kill-shooter-processes.sh"
    Start-Sleep -Seconds 2

    # Clear old log files
    Remove-Item "$PSScriptRoot/../logs/*.log" -ErrorAction SilentlyContinue

    # Use the same simple approach as the successful bash version
    # Resolve the AppHost directory relative to the script location
    $appHostDir = Join-Path (Split-Path $PSScriptRoot -Parent) "Shooter.AppHost"

    # Start rl.sh
    $appHostProc = Start-Process -FilePath "bash" `
        -ArgumentList "./rl.sh" `
        -WorkingDirectory $appHostDir `
        -PassThru

    # Create a simple process list for monitoring
    $processes = @(@{Name="AppHost"; Process=$appHostProc})

    # Wait for Aspire to start all services
    Write-Host "Waiting for Aspire AppHost to start all services..." -ForegroundColor Cyan
    Start-Sleep -Seconds 20

    Write-State "Running" "Services started. Beginning enhanced monitoring..."
    return $processes
}

function Wait-ForAIFix {
    Write-State "Investigating" "Errors captured. Ready for AI analysis."

    # Create instructions for AI
    $instructions = @"
=== AI DEBUGGING INSTRUCTIONS ===

Critical errors detected requiring immediate attention!

Files available for analysis:
- Detailed error report: $errorFile
- Full error context: $contextFile
- Session logs: $workDir/*.log
- Component logs: $PSScriptRoot/../logs/*.log

The enhanced monitoring detected real issues that need fixing.
Please analyze the errors and apply appropriate fixes.

To complete the fix:
1. Read and analyze the error report
2. Identify root causes
3. Apply code fixes
4. Signal completion by writing "FIXED" to: $workDir/fix-complete.txt

Current statistics:
- Iterations: $($stats.Iteration)
- Errors found: $($stats.ErrorsFound)
- Fixes applied: $($stats.FixesApplied)
"@

    $instructions | Out-File $instructionsFile
    Write-Host "`n$instructions" -ForegroundColor Yellow

    if ($AutoFix) {
        Write-Host "`nWaiting for AI to analyze and fix the issue..." -ForegroundColor Cyan

        # Wait for AI to signal fix is complete
        $fixSignalFile = "$workDir/fix-complete.txt"
        $timeout = 300 # 5 minutes
        $waited = 0

        while (-not (Test-Path $fixSignalFile) -and $waited -lt $timeout) {
            Start-Sleep -Seconds 5
            $waited += 5

            if ($waited % 30 -eq 0) {
                Write-Host "Still waiting for fix... ($waited seconds)" -ForegroundColor Gray
            }
        }

        if (Test-Path $fixSignalFile) {
            $stats.FixesApplied++
            Write-State "Fixing" "Fix applied! Restarting to test..."
            Remove-Item $fixSignalFile
            return $true
        }
        else {
            Write-State "Investigating" "Timeout waiting for fix. Manual intervention needed."
            return $false
        }
    }
    else {
        Write-Host "`nPress Enter when you've fixed the issue..." -ForegroundColor Yellow
        Read-Host
        return $true
    }
}

function Stop-AllProcesses {
    & "$PSScriptRoot/../scripts/kill-shooter-processes.sh"
    Start-Sleep -Seconds 2
}

# Main loop
Write-Host "=== ENHANCED AI Development Loop ===" -ForegroundColor Green
Write-Host "Session: $sessionId" -ForegroundColor Cyan
Write-Host "Working directory: $workDir" -ForegroundColor Cyan
Write-Host "Max iterations: $MaxIterations" -ForegroundColor Cyan
Write-Host "Run duration: $RunDuration seconds" -ForegroundColor Cyan
Write-Host "Log check interval: $LogCheckInterval ms" -ForegroundColor Cyan
Write-Host ""

for ($i = 1; $i -le $MaxIterations; $i++) {
    $stats.Iteration = $i
    Write-Host "`n=== Iteration $i ===" -ForegroundColor Yellow

    # Start services
    $processes = Start-ShooterWithLogging

    # Monitor with enhanced error detection
    $success = Monitor-ForErrors -Processes $processes -Duration $RunDuration

    # Stop all processes
    Stop-AllProcesses

    if ($success) {
        Write-Host "`n✓ Iteration $i completed successfully!" -ForegroundColor Green

        # Run longer if stable
        if ($stats.SuccessfulRuns -ge 2) {
            Write-Host "System appears stable. Running extended test..." -ForegroundColor Cyan
            $processes = Start-ShooterWithLogging
            $success = Monitor-ForErrors -Processes $processes -Duration ($RunDuration * 2)
            Stop-AllProcesses

            if ($success) {
                Write-Host "`n✓✓ Extended test passed! System is stable." -ForegroundColor Green
                break
            }
        }
    }
    else {
        # Error found - wait for fix
        if (-not (Wait-ForAIFix)) {
            Write-Host "Fix not applied. Exiting..." -ForegroundColor Red
            break
        }
    }
}

# Final summary
Write-Host "`n=== Session Summary ===" -ForegroundColor Green
Write-Host "Total iterations: $($stats.Iteration)" -ForegroundColor Cyan
Write-Host "Errors found: $($stats.ErrorsFound)" -ForegroundColor $(if ($stats.ErrorsFound -gt 0) { "Yellow" } else { "Green" })
Write-Host "Fixes applied: $($stats.FixesApplied)" -ForegroundColor Cyan
Write-Host "Successful runs: $($stats.SuccessfulRuns)" -ForegroundColor Green
Write-Host "Session data: $workDir" -ForegroundColor Gray

Write-State "Complete" "Session finished"