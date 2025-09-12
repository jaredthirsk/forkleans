#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Simple real-time log tailing for Shooter processes with error highlighting.

.DESCRIPTION
    Tails the console output of running Shooter processes and highlights errors/warnings.

.PARAMETER Component
    Which component to tail (Silo, Client, ActionServer, Bot, All)
    Default: All

.PARAMETER ErrorOnly
    Only show errors and warnings
    Default: false

.PARAMETER Follow
    Keep following the log (like tail -f)
    Default: true

.EXAMPLE
    ./tail-shooter-logs.ps1 -Component Client -ErrorOnly
#>
param(
    [ValidateSet("Silo", "Client", "ActionServer", "Bot", "All")]
    [string]$Component = "All",
    [switch]$ErrorOnly,
    [bool]$Follow = $true
)

$ErrorActionPreference = "Stop"

# Color coding
$colors = @{
    "ERROR" = "Red"
    "FATAL" = "DarkRed"
    "WARN" = "Yellow"
    "WARNING" = "Yellow"
    "INFO" = "Cyan"
    "DEBUG" = "Gray"
    "TRACE" = "DarkGray"
    "Exception" = "Magenta"
}

function Get-ShooterProcess {
    param($Name)
    
    $proc = Get-Process -Name "Shooter.$Name" -ErrorAction SilentlyContinue
    if ($proc) {
        return @{
            Name = $Name
            Process = $proc
            PID = $proc.Id
        }
    }
    return $null
}

function Format-LogLine {
    param($Line, $Component)
    
    # Determine color based on content
    $color = "White"
    $prefix = ""
    
    foreach ($key in $colors.Keys) {
        if ($Line -match $key) {
            $color = $colors[$key]
            $prefix = "[$key]"
            break
        }
    }
    
    # Format timestamp if present
    if ($Line -match '^\[?(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}[\.\d]*)\]?(.*)') {
        $timestamp = $matches[1]
        $message = $matches[2]
        return @{
            Text = "[$Component] $prefix$message"
            Color = $color
            Time = $timestamp
        }
    }
    
    return @{
        Text = "[$Component] $prefix $Line"
        Color = $color
    }
}

function Watch-Process {
    param($ProcessInfo)
    
    $name = $ProcessInfo.Name
    $pid = $ProcessInfo.PID
    
    Write-Host "Watching $name (PID: $pid)" -ForegroundColor Green
    
    # Try to attach to console output
    # Note: This is simplified - in practice we'd need to redirect stdout/stderr
    # or use ETW tracing for already-running processes
    
    Write-Host "Note: For already-running processes, consider using Event Tracing or restart with logging" -ForegroundColor Yellow
}

# Main
Write-Host "=== Shooter Log Tail ===" -ForegroundColor Green
Write-Host "Component: $Component" -ForegroundColor Cyan
Write-Host "Error Only: $ErrorOnly" -ForegroundColor Cyan
Write-Host "Follow: $Follow`n" -ForegroundColor Cyan

# Find processes
$processes = @()

if ($Component -eq "All") {
    $componentNames = @("Silo", "Client", "ActionServer", "Bot")
} else {
    $componentNames = @($Component)
}

foreach ($name in $componentNames) {
    $proc = Get-ShooterProcess -Name $name
    if ($proc) {
        $processes += $proc
        Write-Host "Found $name process (PID: $($proc.PID))" -ForegroundColor Green
    } else {
        Write-Host "$name not running" -ForegroundColor Gray
    }
}

if ($processes.Count -eq 0) {
    Write-Host "`nNo Shooter processes found running!" -ForegroundColor Red
    Write-Host "Start them first or use monitor-shooter-logs.ps1 with -RunMode Start" -ForegroundColor Yellow
    exit 1
}

Write-Host "`nTailing logs... Press Ctrl+C to stop`n" -ForegroundColor Yellow

# For demonstration, let's tail actual log files if they exist
$logDir = "../logs"
$tempDir = "../obj"

# Simple approach: look for recent log files
$recentLogs = @()

foreach ($proc in $processes) {
    # Look for log files
    $patterns = @(
        "$logDir/Shooter.$($proc.Name)*.log",
        "$tempDir/Shooter.$($proc.Name)*.log",
        "../Shooter.$($proc.Name)/bin/Debug/net8.0/*.log"
    )
    
    foreach ($pattern in $patterns) {
        $files = Get-ChildItem -Path $pattern -ErrorAction SilentlyContinue | 
                 Sort-Object LastWriteTime -Descending |
                 Select-Object -First 1
        
        if ($files) {
            $recentLogs += @{
                Component = $proc.Name
                File = $files[0].FullName
            }
            Write-Host "Found log: $($files[0].Name)" -ForegroundColor Gray
        }
    }
}

if ($recentLogs.Count -eq 0) {
    Write-Host "No log files found. Processes may be logging to console only." -ForegroundColor Yellow
    Write-Host "Consider running with monitor-shooter-logs.ps1 for full logging." -ForegroundColor Yellow
    exit 1
}

# Tail the logs
try {
    # Create jobs for each log file
    $jobs = @()
    foreach ($log in $recentLogs) {
        $job = Start-Job -ScriptBlock {
            param($File, $Component)
            Get-Content $File -Tail 10 -Wait | ForEach-Object {
                [PSCustomObject]@{
                    Component = $Component
                    Line = $_
                }
            }
        } -ArgumentList $log.File, $log.Component
        $jobs += $job
    }
    
    # Process output
    while ($true) {
        foreach ($job in $jobs) {
            $output = Receive-Job -Job $job -ErrorAction SilentlyContinue
            foreach ($item in $output) {
                $formatted = Format-LogLine -Line $item.Line -Component $item.Component
                
                # Apply filtering
                if ($ErrorOnly) {
                    if ($formatted.Color -in @("Red", "DarkRed", "Yellow", "Magenta")) {
                        Write-Host $formatted.Text -ForegroundColor $formatted.Color
                    }
                } else {
                    Write-Host $formatted.Text -ForegroundColor $formatted.Color
                }
            }
        }
        
        Start-Sleep -Milliseconds 100
        
        if (-not $Follow) {
            break
        }
    }
}
finally {
    # Clean up jobs
    $jobs | Stop-Job
    $jobs | Remove-Job
}

Write-Host "`nLog tailing stopped" -ForegroundColor Green