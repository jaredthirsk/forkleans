#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Comprehensive cleaning script for Granville Orleans with fine-grained control.

.DESCRIPTION
    This script provides various cleaning options for the Granville Orleans repository:
    - Clean bin and obj directories
    - Clear NuGet caches (opt-in only)
    - Remove temporary directories
    - Clean log files
    - Clean Artifacts/Release directory
    - By default, cleans everything except NuGet cache

.PARAMETER All
    Clean everything including Artifacts/Release (default behavior)
    Default: true

.PARAMETER BinObj
    Clean only bin and obj directories
    Default: false (unless specified, All takes precedence)

.PARAMETER NuGetCache
    Clean only NuGet caches
    Default: false (unless specified, All takes precedence)

.PARAMETER Temp
    Clean only temporary directories
    Default: false (unless specified, All takes precedence)

.PARAMETER Logs
    Clean only log files
    Default: false (unless specified, All takes precedence)

.PARAMETER Artifacts
    Clean Artifacts/Release directory
    Default: false (unless -All is specified)

.PARAMETER DryRun
    Show what would be deleted without actually deleting
    Default: false

.PARAMETER Force
    Delete without confirmation prompt
    Default: false

.PARAMETER Quiet
    Suppress detailed output, show only summary
    Default: false

.EXAMPLE
    ./clean.ps1
    Cleans bin/obj, temp dirs, logs, and Artifacts but NOT NuGet cache

.EXAMPLE
    ./clean.ps1 -All
    Cleans absolutely everything including NuGet cache

.EXAMPLE
    ./clean.ps1 -BinObj
    Cleans only bin and obj directories

.EXAMPLE
    ./clean.ps1 -BinObj -NuGetCache
    Cleans bin/obj directories and NuGet caches

.EXAMPLE
    ./clean.ps1 -DryRun
    Shows what would be cleaned without actually deleting anything

.EXAMPLE
    ./clean.ps1 -Artifacts
    Cleans only the Artifacts/Release directory
#>

param(
    [switch]$All = $false,
    [switch]$BinObj = $false,
    [switch]$NuGetCache = $false,
    [switch]$Temp = $false,
    [switch]$Logs = $false,
    [switch]$Artifacts = $false,
    [switch]$DryRun = $false,
    [switch]$Force = $false,
    [switch]$Quiet = $false
)

$ErrorActionPreference = "Stop"

# If no specific options are provided, default to cleaning everything except NuGet cache
$noOptionsSpecified = -not ($BinObj -or $NuGetCache -or $Temp -or $Logs -or $Artifacts)
if ($noOptionsSpecified -and -not $All) {
    # Default behavior: clean everything except NuGet cache
    $BinObj = $true
    $NuGetCache = $false
    $Temp = $true
    $Logs = $true
    $Artifacts = $true
}
elseif ($All) {
    # -All explicitly specified: clean everything including NuGet cache
    $BinObj = $true
    $NuGetCache = $true
    $Temp = $true
    $Logs = $true
    $Artifacts = $true
}

# Get the repository root (two directories up from the script location)
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Push-Location $repoRoot

try {
    Write-Host "Granville Orleans Cleaning Utility" -ForegroundColor Cyan
    Write-Host "===================================" -ForegroundColor Cyan
    
    if ($DryRun) {
        Write-Host "DRY RUN MODE - No files will be deleted" -ForegroundColor Magenta
    }
    
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
    
    $totalItemsRemoved = 0
    
    # Clean NuGet caches
    if ($NuGetCache) {
        Write-Host "`nCleaning NuGet caches..." -ForegroundColor Yellow
        if (-not $Quiet) {
            Write-Host "  Using: $dotnetCmd" -ForegroundColor Gray
        }
        
        if (-not $DryRun) {
            & $dotnetCmd nuget locals all --clear | Out-Null
            Write-Host "  ✓ NuGet caches cleared" -ForegroundColor Green
        }
        else {
            Write-Host "  Would clear all NuGet local caches" -ForegroundColor DarkYellow
        }
    }
    
    # Clean bin and obj directories
    if ($BinObj) {
        Write-Host "`nCleaning bin and obj directories..." -ForegroundColor Yellow
        
        $binObjDirs = Get-ChildItem -Path . -Include bin,obj -Recurse -Directory | 
                      Where-Object { $_.FullName -notlike "*node_modules*" }
        
        if ($binObjDirs.Count -gt 0) {
            if (-not $Quiet) {
                # Group by parent directory for better display
                $grouped = $binObjDirs | Group-Object { $_.Parent.FullName }
                foreach ($group in $grouped | Select-Object -First 10) {
                    $relativePath = $group.Name.Replace($repoRoot, "").TrimStart("\", "/")
                    if ([string]::IsNullOrWhiteSpace($relativePath)) {
                        $relativePath = "."
                    }
                    $folders = $group.Group | ForEach-Object { $_.Name }
                    Write-Host "  $relativePath -> $($folders -join ", ")" -ForegroundColor Gray
                }
                if ($grouped.Count -gt 10) {
                    Write-Host "  ... and $($grouped.Count - 10) more locations" -ForegroundColor DarkGray
                }
            }
            
            Write-Host "  Found: $($binObjDirs.Count) directories" -ForegroundColor DarkYellow
            
            if (-not $DryRun) {
                $removed = 0
                foreach ($dir in $binObjDirs) {
                    try {
                        Remove-Item $dir.FullName -Recurse -Force -ErrorAction Stop
                        $removed++
                        if (-not $Quiet) { Write-Host "." -NoNewline -ForegroundColor Green }
                    }
                    catch {
                        if (-not $Quiet) { Write-Host "x" -NoNewline -ForegroundColor Red }
                    }
                }
                if (-not $Quiet) { Write-Host "" }
                Write-Host "  ✓ Removed $removed directories" -ForegroundColor Green
                $totalItemsRemoved += $removed
            }
        }
        else {
            Write-Host "  No bin/obj directories found" -ForegroundColor Gray
        }
    }
    
    # Clean temporary directories
    if ($Temp) {
        Write-Host "`nCleaning temporary directories..." -ForegroundColor Yellow
        
        $tempPatterns = @(
            "granville/compatibility-tools/temp-*",
            "granville/compatibility-tools/shims-proper",
            "granville/scripts/temp-*",
            "temp-*",
            "TestResults",
            "Artifacts/DistributedTests"
        )
        
        $tempDirs = @()
        foreach ($pattern in $tempPatterns) {
            $tempDirs += Get-ChildItem -Path . -Filter $pattern -Recurse -Directory -ErrorAction SilentlyContinue
        }
        
        if ($tempDirs.Count -gt 0) {
            if (-not $Quiet) {
                foreach ($dir in $tempDirs | Select-Object -First 5) {
                    $relativePath = $dir.FullName.Replace($repoRoot, "").TrimStart("\", "/")
                    Write-Host "  $relativePath" -ForegroundColor Gray
                }
                if ($tempDirs.Count -gt 5) {
                    Write-Host "  ... and $($tempDirs.Count - 5) more" -ForegroundColor DarkGray
                }
            }
            
            Write-Host "  Found: $($tempDirs.Count) directories" -ForegroundColor DarkYellow
            
            if (-not $DryRun) {
                $removed = 0
                foreach ($dir in $tempDirs) {
                    try {
                        Remove-Item $dir.FullName -Recurse -Force -ErrorAction Stop
                        $removed++
                    }
                    catch {
                        # Silently continue
                    }
                }
                Write-Host "  ✓ Removed $removed directories" -ForegroundColor Green
                $totalItemsRemoved += $removed
            }
        }
        else {
            Write-Host "  No temporary directories found" -ForegroundColor Gray
        }
    }
    
    # Clean log files
    if ($Logs) {
        Write-Host "`nCleaning log files..." -ForegroundColor Yellow
        
        $logFiles = @()
        $logFiles += Get-ChildItem -Path . -Filter "*.log" -Recurse -File -ErrorAction SilentlyContinue |
                     Where-Object { $_.FullName -notlike "*node_modules*" }
        
        if ($logFiles.Count -gt 0) {
            if (-not $Quiet -and $logFiles.Count -le 10) {
                foreach ($file in $logFiles) {
                    $relativePath = $file.FullName.Replace($repoRoot, "").TrimStart("\", "/")
                    Write-Host "  $relativePath" -ForegroundColor Gray
                }
            }
            elseif (-not $Quiet) {
                Write-Host "  Found logs in multiple locations" -ForegroundColor Gray
            }
            
            Write-Host "  Found: $($logFiles.Count) files" -ForegroundColor DarkYellow
            
            if (-not $DryRun) {
                $removed = 0
                foreach ($file in $logFiles) {
                    try {
                        Remove-Item $file.FullName -Force -ErrorAction Stop
                        $removed++
                    }
                    catch {
                        # Silently continue
                    }
                }
                Write-Host "  ✓ Removed $removed files" -ForegroundColor Green
                $totalItemsRemoved += $removed
            }
        }
        else {
            Write-Host "  No log files found" -ForegroundColor Gray
        }
    }
    
    # Clean Artifacts/Release
    if ($Artifacts) {
        Write-Host "`nCleaning Artifacts/Release..." -ForegroundColor Yellow
        
        $artifactsPath = Join-Path $repoRoot "Artifacts/Release"
        if (Test-Path $artifactsPath) {
            $artifactFiles = (Get-ChildItem $artifactsPath -File -ErrorAction SilentlyContinue).Count
            
            Write-Host "  Found: $artifactFiles files" -ForegroundColor DarkYellow
            
            if (-not $DryRun) {
                try {
                    Remove-Item "$artifactsPath/*" -Recurse -Force -ErrorAction Stop
                    Write-Host "  ✓ Artifacts cleaned" -ForegroundColor Green
                    $totalItemsRemoved += $artifactFiles
                }
                catch {
                    Write-Host "  ⚠ Some artifacts could not be removed" -ForegroundColor Red
                }
            }
        }
        else {
            Write-Host "  Artifacts directory not found" -ForegroundColor Gray
        }
    }
    
    # Summary
    Write-Host "`n===================================" -ForegroundColor Cyan
    if ($DryRun) {
        Write-Host "DRY RUN SUMMARY" -ForegroundColor Magenta
        Write-Host "Would remove: $totalItemsRemoved items" -ForegroundColor Yellow
    }
    else {
        Write-Host "CLEANING COMPLETE" -ForegroundColor Green
        if ($totalItemsRemoved -gt 0) {
            Write-Host "Removed: $totalItemsRemoved items" -ForegroundColor Green
        }
        else {
            Write-Host "Nothing to clean!" -ForegroundColor Green
        }
    }
    
    # Next steps hint
    if (-not $DryRun -and $BinObj) {
        Write-Host "`nNext steps:" -ForegroundColor Cyan
        Write-Host "  • Run 'dotnet restore' to restore packages" -ForegroundColor Gray
        Write-Host "  • Run './granville/scripts/build-all-granville.ps1' to rebuild" -ForegroundColor Gray
    }
}
finally {
    Pop-Location
}

# Ensure proper exit code
exit 0