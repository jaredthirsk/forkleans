#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Diagnoses zone transition and connectivity issues in the Shooter application

.DESCRIPTION
    This script analyzes logs to identify:
    - Zone mismatch errors (PROLONGED_MISMATCH, CHRONIC_MISMATCH)
    - SSL certificate issues
    - RPC connection failures
    - Bot connectivity problems
    - Zone transition deadlocks

.PARAMETER LogDir
    Directory containing log files (default: ../logs)

.PARAMETER ShowRecommendations
    Show recommended fixes for detected issues

.EXAMPLE
    ./diagnose-zone-issues.ps1
    ./diagnose-zone-issues.ps1 -ShowRecommendations
#>
param(
    [string]$LogDir = "$PSScriptRoot/../logs",
    [switch]$ShowRecommendations
)

$ErrorActionPreference = "Stop"

Write-Host "=== Zone Transition & Connectivity Diagnostics ===" -ForegroundColor Green
Write-Host "Analyzing logs in: $LogDir" -ForegroundColor Cyan
Write-Host ""

# Initialize issue tracking
$issues = @{
    ZoneMismatch = @()
    SSLCertificate = @()
    RPCFailure = @()
    BotConnection = @()
    ZoneDeadlock = @()
    Other = @()
}

$stats = @{
    TotalErrors = 0
    FilesAnalyzed = 0
    TimeRange = @{
        Start = $null
        End = $null
    }
}

function Analyze-LogFile {
    param($FilePath)

    if (-not (Test-Path $FilePath)) {
        return
    }

    $fileName = Split-Path $FilePath -Leaf
    $stats.FilesAnalyzed++

    # Get last 500 lines for analysis
    $lines = Get-Content $FilePath -Tail 500 -ErrorAction SilentlyContinue
    if (-not $lines) { return }

    foreach ($line in $lines) {
        # Extract timestamp if present
        if ($line -match '^\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}') {
            $timestamp = $matches[0]
            if (-not $stats.TimeRange.Start -or $timestamp -lt $stats.TimeRange.Start) {
                $stats.TimeRange.Start = $timestamp
            }
            if (-not $stats.TimeRange.End -or $timestamp -gt $stats.TimeRange.End) {
                $stats.TimeRange.End = $timestamp
            }
        }

        # Zone Mismatch Issues
        if ($line -match 'PROLONGED_MISMATCH') {
            $stats.TotalErrors++
            if ($line -match 'Player in zone \((\d+),(\d+)\) but connected to server for zone \((\d+),(\d+)\)') {
                $issues.ZoneMismatch += @{
                    Type = "PROLONGED_MISMATCH"
                    PlayerZone = "($($matches[1]),$($matches[2]))"
                    ServerZone = "($($matches[3]),$($matches[4]))"
                    File = $fileName
                    Line = $line
                }
            }
        }
        elseif ($line -match 'CHRONIC_MISMATCH') {
            $stats.TotalErrors++
            $issues.ZoneMismatch += @{
                Type = "CHRONIC_MISMATCH"
                File = $fileName
                Line = $line
            }
        }
        elseif ($line -match 'STUCK_TRANSITION') {
            $stats.TotalErrors++
            $issues.ZoneDeadlock += @{
                Type = "STUCK_TRANSITION"
                File = $fileName
                Line = $line
            }
        }

        # SSL Certificate Issues
        elseif ($line -match 'SSL connection could not be established' -or
                $line -match 'UntrustedRoot' -or
                $line -match 'certificate.*invalid') {
            $stats.TotalErrors++
            $issues.SSLCertificate += @{
                File = $fileName
                Line = $line
            }
        }

        # RPC Failures
        elseif ($line -match 'RPC failed' -or
                $line -match 'Failed to connect to RPC' -or
                $line -match 'RPC.*timeout') {
            $stats.TotalErrors++
            $issues.RPCFailure += @{
                File = $fileName
                Line = $line
            }
        }

        # Bot Connection Issues
        elseif ($line -match 'Bot.*failed to connect' -or
                $line -match 'Bot SignalR connection closed' -or
                $line -match 'Error registering player.*Bot') {
            $stats.TotalErrors++
            $issues.BotConnection += @{
                File = $fileName
                Line = $line
            }
        }

        # Other Critical Errors
        elseif ($line -match '\[Error\]' -or $line -match 'Exception:') {
            if ($line -notmatch 'Polly.*Retry') {  # Ignore retry warnings
                $stats.TotalErrors++
                $issues.Other += @{
                    File = $fileName
                    Line = $line.Substring(0, [Math]::Min($line.Length, 200))
                }
            }
        }
    }
}

# Analyze all log files
Write-Host "Analyzing log files..." -ForegroundColor Yellow
Get-ChildItem "$LogDir/*.log" -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "  - $($_.Name)" -NoNewline
    Analyze-LogFile $_.FullName
    Write-Host " ✓" -ForegroundColor Green
}

Write-Host ""
Write-Host "=== Diagnostic Report ===" -ForegroundColor Green
Write-Host "Time Range: $($stats.TimeRange.Start) - $($stats.TimeRange.End)" -ForegroundColor Gray
Write-Host "Files Analyzed: $($stats.FilesAnalyzed)" -ForegroundColor Gray
Write-Host "Total Issues Found: $($stats.TotalErrors)" -ForegroundColor $(if ($stats.TotalErrors -gt 0) { "Yellow" } else { "Green" })
Write-Host ""

# Report Zone Mismatch Issues
if ($issues.ZoneMismatch.Count -gt 0) {
    Write-Host "❌ Zone Mismatch Issues ($($issues.ZoneMismatch.Count))" -ForegroundColor Red
    $grouped = $issues.ZoneMismatch | Group-Object -Property Type
    foreach ($group in $grouped) {
        Write-Host "  - $($group.Name): $($group.Count) occurrences" -ForegroundColor Yellow
        if ($group.Name -eq "PROLONGED_MISMATCH") {
            $zones = $group.Group | Group-Object -Property { "$($_.PlayerZone)->$($_.ServerZone)" }
            foreach ($zone in $zones) {
                Write-Host "    • $($zone.Name): $($zone.Count) times" -ForegroundColor Gray
            }
        }
    }
    Write-Host ""
}

# Report SSL Certificate Issues
if ($issues.SSLCertificate.Count -gt 0) {
    Write-Host "❌ SSL Certificate Issues ($($issues.SSLCertificate.Count))" -ForegroundColor Red
    $files = $issues.SSLCertificate | Group-Object -Property File
    foreach ($file in $files) {
        Write-Host "  - $($file.Name): $($file.Count) errors" -ForegroundColor Yellow
    }
    Write-Host ""
}

# Report RPC Failures
if ($issues.RPCFailure.Count -gt 0) {
    Write-Host "❌ RPC Connection Failures ($($issues.RPCFailure.Count))" -ForegroundColor Red
    $files = $issues.RPCFailure | Group-Object -Property File
    foreach ($file in $files) {
        Write-Host "  - $($file.Name): $($file.Count) failures" -ForegroundColor Yellow
    }
    Write-Host ""
}

# Report Bot Connection Issues
if ($issues.BotConnection.Count -gt 0) {
    Write-Host "❌ Bot Connection Issues ($($issues.BotConnection.Count))" -ForegroundColor Red
    Write-Host ""
}

# Report Zone Deadlocks
if ($issues.ZoneDeadlock.Count -gt 0) {
    Write-Host "❌ Zone Transition Deadlocks ($($issues.ZoneDeadlock.Count))" -ForegroundColor Red
    Write-Host ""
}

# Show recommendations if requested
if ($ShowRecommendations) {
    Write-Host "=== Recommended Fixes ===" -ForegroundColor Cyan

    if ($issues.ZoneMismatch.Count -gt 0) {
        Write-Host "`n📋 Zone Mismatch Fixes:" -ForegroundColor Yellow
        Write-Host "  1. Check zone assignment logic in WorldGrain" -ForegroundColor Gray
        Write-Host "  2. Verify ActionServer zone mappings are correct" -ForegroundColor Gray
        Write-Host "  3. Review ZoneTransitionDebouncer thresholds" -ForegroundColor Gray
        Write-Host "  4. Consider implementing forced reconnection after prolonged mismatch" -ForegroundColor Gray
    }

    if ($issues.SSLCertificate.Count -gt 0) {
        Write-Host "`n📋 SSL Certificate Fixes:" -ForegroundColor Yellow
        Write-Host "  1. Run: dotnet dev-certs https --trust" -ForegroundColor Gray
        Write-Host "  2. Or run: $PSScriptRoot/trust-dev-cert.sh" -ForegroundColor Gray
        Write-Host "  3. Ensure all services use consistent HTTPS configuration" -ForegroundColor Gray
        Write-Host "  4. Check that bot HttpClient handlers bypass cert validation in dev" -ForegroundColor Gray
    }

    if ($issues.RPCFailure.Count -gt 0) {
        Write-Host "`n📋 RPC Connection Fixes:" -ForegroundColor Yellow
        Write-Host "  1. Verify UDP ports are not blocked" -ForegroundColor Gray
        Write-Host "  2. Check RPC server registration in ActionServers" -ForegroundColor Gray
        Write-Host "  3. Review RPC client connection timeout settings" -ForegroundColor Gray
        Write-Host "  4. Ensure zone-to-server mappings are up to date" -ForegroundColor Gray
    }

    if ($issues.ZoneDeadlock.Count -gt 0) {
        Write-Host "`n📋 Zone Deadlock Fixes:" -ForegroundColor Yellow
        Write-Host "  1. Add timeout to zone transition operations" -ForegroundColor Gray
        Write-Host "  2. Implement deadlock detection and recovery" -ForegroundColor Gray
        Write-Host "  3. Review _transitionLock usage in GranvilleRpcGameClientService" -ForegroundColor Gray
        Write-Host "  4. Consider async lock alternatives" -ForegroundColor Gray
    }
}

# Summary
Write-Host "`n=== Summary ===" -ForegroundColor Green
if ($stats.TotalErrors -eq 0) {
    Write-Host "✅ No critical issues detected!" -ForegroundColor Green
    Write-Host "System appears to be running normally." -ForegroundColor Gray
}
else {
    Write-Host "⚠️  Found $($stats.TotalErrors) issues requiring attention" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Priority issues to address:" -ForegroundColor Cyan

    $priority = 1
    if ($issues.ZoneMismatch.Count -gt 0) {
        Write-Host "  $priority. Fix zone transition mismatches (affects gameplay)" -ForegroundColor Yellow
        $priority++
    }
    if ($issues.SSLCertificate.Count -gt 0) {
        Write-Host "  $priority. Resolve SSL certificate trust (prevents bot connection)" -ForegroundColor Yellow
        $priority++
    }
    if ($issues.RPCFailure.Count -gt 0) {
        Write-Host "  $priority. Fix RPC connection issues (blocks game communication)" -ForegroundColor Yellow
        $priority++
    }
}

Write-Host ""
Write-Host "Run with -ShowRecommendations for detailed fix suggestions" -ForegroundColor Gray