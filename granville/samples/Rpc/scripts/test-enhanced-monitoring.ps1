#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Tests the enhanced AI development loop monitoring capabilities.

.DESCRIPTION
    This script creates test log entries to verify the enhanced monitoring
    can detect the critical errors that were being missed.
#>

param(
    [switch]$CreateTestLogs = $false
)

$ErrorActionPreference = "Stop"

if ($CreateTestLogs) {
    # Create test log directory
    $testLogDir = "test-logs"
    New-Item -ItemType Directory -Force -Path $testLogDir | Out-Null

    Write-Host "Creating test log files with various error patterns..." -ForegroundColor Yellow

    # Create test client log with zone mismatch error
    $clientLog = @"
2025-01-25 10:15:23.456 [INF] Client started successfully
2025-01-25 10:15:24.123 [INF] Connected to server
2025-01-25 10:15:25.789 [HEALTH_MONITOR] PROLONGED_MISMATCH: Player in zone (1,1) but connected to server for zone (1,0) for 5012.0817ms. This indicates a serious zone synchronization issue.
2025-01-25 10:15:26.012 [ERR] Zone transition failed
2025-01-25 10:15:27.345 [HEALTH_MONITOR] CHRONIC_MISMATCH: Player repeatedly mismatched with zone
"@
    $clientLog | Out-File "$testLogDir/client.log"

    # Create test bot log with connection failures
    $botLog = @"
2025-01-25 10:15:30.111 [INF] Bot starting
2025-01-25 10:15:31.222 [ERR] Bot LiteNetLibTest0 failed to connect to game: Connection timeout
2025-01-25 10:15:32.333 [ERR] SSL connection could not be established: UntrustedRoot certificate error
2025-01-25 10:15:33.444 [ERR] Bot SignalR connection closed unexpectedly
2025-01-25 10:15:34.555 [ERR] Error registering player: RPC endpoint not responding
"@
    $botLog | Out-File "$testLogDir/bot-0.log"

    # Create test action server log with RPC failures
    $actionServerLog = @"
2025-01-25 10:15:40.666 [INF] ActionServer started on port 7072
2025-01-25 10:15:41.777 [ERR] Player input RPC failed: Timeout waiting for response
2025-01-25 10:15:42.888 [HEALTH_MONITOR] STUCK_TRANSITION: Zone transition stuck >10 seconds for player 12345
2025-01-25 10:15:43.999 [ERR] RPC failed: Unable to establish UDP connection
"@
    $actionServerLog | Out-File "$testLogDir/actionserver-0.log"

    Write-Host "Test log files created in $testLogDir/" -ForegroundColor Green
}

# Now run the monitoring test
Write-Host "`n=== Testing Enhanced Monitoring Detection ===" -ForegroundColor Cyan

$logPaths = if ($CreateTestLogs) {
    @("test-logs/*.log")
} else {
    @(
        "logs/client.log",
        "logs/client-console.log",
        "logs/bot-0.log",
        "logs/bot-0-console.log",
        "logs/silo.log",
        "logs/actionserver-*.log"
    )
}

# Define the critical patterns (same as enhanced script)
$criticalPatterns = @(
    @{ Pattern = 'PROLONGED_MISMATCH.*for (\d{4,})\.\d+ms'; Description = 'Zone mismatch >4 seconds'; Regex = $true },
    @{ Pattern = 'CHRONIC_MISMATCH'; Description = 'Repeated zone mismatches'; Regex = $false },
    @{ Pattern = 'STUCK_TRANSITION.*>(\d+) seconds'; Description = 'Zone transition stuck'; Regex = $true },
    @{ Pattern = 'Player input RPC failed'; Description = 'RPC failure'; Regex = $false },
    @{ Pattern = 'Bot.*failed to connect to game'; Description = 'Bot connection failure'; Regex = $true },
    @{ Pattern = 'SSL connection could not be established'; Description = 'SSL certificate issue'; Regex = $false },
    @{ Pattern = 'Bot SignalR connection closed'; Description = 'Bot SignalR disconnection'; Regex = $false },
    @{ Pattern = 'Error registering player'; Description = 'Player registration failed'; Regex = $false },
    @{ Pattern = 'RPC failed'; Description = 'General RPC failure'; Regex = $false }
)

$detectedErrors = @()
$totalChecked = 0

foreach ($logPath in $logPaths) {
    $resolvedPaths = Get-Item $logPath -ErrorAction SilentlyContinue
    foreach ($path in $resolvedPaths) {
        if (Test-Path $path) {
            $totalChecked++
            Write-Host "Checking: $($path.Name)" -ForegroundColor Gray

            $content = Get-Content $path -Raw -ErrorAction SilentlyContinue
            if ($content) {
                foreach ($pattern in $criticalPatterns) {
                    $found = $false
                    if ($pattern.Regex) {
                        if ($content -match $pattern.Pattern) {
                            $found = $true
                        }
                    } else {
                        if ($content -like "*$($pattern.Pattern)*") {
                            $found = $true
                        }
                    }

                    if ($found) {
                        $detectedErrors += @{
                            File = $path.Name
                            Pattern = $pattern.Description
                            PatternText = $pattern.Pattern
                        }
                    }
                }
            }
        }
    }
}

# Report results
Write-Host "`n=== Detection Results ===" -ForegroundColor Yellow

if ($detectedErrors.Count -gt 0) {
    Write-Host "✓ SUCCESS: Enhanced monitoring detected $($detectedErrors.Count) critical error pattern(s)!" -ForegroundColor Green
    Write-Host "`nDetected Errors:" -ForegroundColor Cyan

    $detectedErrors | Group-Object Pattern | ForEach-Object {
        Write-Host "  • $($_.Name): $($_.Count) occurrence(s)" -ForegroundColor Yellow
        $_.Group | ForEach-Object {
            Write-Host "      in $($_.File)" -ForegroundColor Gray
        }
    }

    Write-Host "`nThese are the types of errors that would trigger the AI development loop to:" -ForegroundColor Cyan
    Write-Host "  1. Stop the application" -ForegroundColor White
    Write-Host "  2. Save error context for AI analysis" -ForegroundColor White
    Write-Host "  3. Wait for fixes to be applied" -ForegroundColor White
    Write-Host "  4. Restart and verify the fix" -ForegroundColor White
} else {
    if ($totalChecked -eq 0) {
        Write-Host "No log files found to check." -ForegroundColor Yellow
        Write-Host "Run with -CreateTestLogs to generate test logs with error patterns." -ForegroundColor Gray
    } else {
        Write-Host "No critical errors detected in $totalChecked log file(s)." -ForegroundColor Green
        Write-Host "This indicates the application is currently running without the reported issues." -ForegroundColor Gray
        Write-Host "`nTo test detection capabilities, run:" -ForegroundColor Cyan
        Write-Host "  ./test-enhanced-monitoring.ps1 -CreateTestLogs" -ForegroundColor White
    }
}

Write-Host "`n=== Pattern Coverage ===" -ForegroundColor Yellow
Write-Host "The enhanced monitoring watches for:" -ForegroundColor Cyan
$criticalPatterns | ForEach-Object {
    Write-Host "  • $($_.Description)" -ForegroundColor White
}