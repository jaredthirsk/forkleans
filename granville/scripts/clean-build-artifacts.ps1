#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Cleans all bin and obj folders in the repository.

.DESCRIPTION
    This script removes all bin and obj folders to ensure a clean build.
    Useful when dealing with version mismatches or stale build artifacts.

.PARAMETER DryRun
    Show what would be deleted without actually deleting

.PARAMETER Force
    Delete without confirmation prompt
#>

param(
    [switch]$DryRun = $false,
    [switch]$Force = $false
)

$ErrorActionPreference = "Stop"

Write-Host "Cleaning build artifacts..." -ForegroundColor Cyan

# Get the script directory (repository root)
$repoRoot = $PSScriptRoot

# Find all bin and obj folders
$foldersToDelete = @()
$foldersToDelete += Get-ChildItem -Path $repoRoot -Directory -Recurse -Include "bin", "obj" | 
    Where-Object { $_.FullName -notlike "*node_modules*" }

if ($foldersToDelete.Count -eq 0) {
    Write-Host "No bin or obj folders found." -ForegroundColor Green
    exit 0
}

Write-Host "Found $($foldersToDelete.Count) folders to clean:" -ForegroundColor Yellow

# Group by parent directory for better display
$grouped = $foldersToDelete | Group-Object { $_.Parent.FullName }

foreach ($group in $grouped) {
    $relativePath = $group.Name.Replace($repoRoot, "").TrimStart("\", "/")
    if ([string]::IsNullOrWhiteSpace($relativePath)) {
        $relativePath = "."
    }
    $folders = $group.Group | ForEach-Object { $_.Name }
    Write-Host "  $relativePath" -ForegroundColor Gray -NoNewline
    Write-Host " -> " -ForegroundColor DarkGray -NoNewline
    Write-Host ($folders -join ", ") -ForegroundColor DarkYellow
}

if ($DryRun) {
    Write-Host "`nDry run mode - no files will be deleted." -ForegroundColor Magenta
    Write-Host "Total folders that would be deleted: $($foldersToDelete.Count)" -ForegroundColor Yellow
    exit 0
}

# Confirm before deleting
if (-not $Force) {
    Write-Host "`nThis will delete $($foldersToDelete.Count) folders." -ForegroundColor Red
    $confirmation = Read-Host "Are you sure? (y/N)"

    if ($confirmation -ne 'y') {
        Write-Host "Operation cancelled." -ForegroundColor Yellow
        exit 0
    }
} else {
    Write-Host "`nDeleting $($foldersToDelete.Count) folders..." -ForegroundColor Yellow
}

# Delete the folders
$deletedCount = 0
$failedCount = 0

foreach ($folder in $foldersToDelete) {
    try {
        Remove-Item -Path $folder.FullName -Recurse -Force -ErrorAction Stop
        $deletedCount++
        Write-Host "." -NoNewline -ForegroundColor Green
    }
    catch {
        $failedCount++
        Write-Host "x" -NoNewline -ForegroundColor Red
        Write-Host "`nFailed to delete: $($folder.FullName)" -ForegroundColor Red
        Write-Host "  Error: $_" -ForegroundColor DarkRed
    }
}

Write-Host ""
Write-Host "`nCleanup complete!" -ForegroundColor Green
Write-Host "  Deleted: $deletedCount folders" -ForegroundColor Green
if ($failedCount -gt 0) {
    Write-Host "  Failed: $failedCount folders" -ForegroundColor Red
}

Write-Host "`nNext steps:" -ForegroundColor Cyan
Write-Host "  1. Run 'dotnet restore' to restore packages" -ForegroundColor Gray
Write-Host "  2. Run 'dotnet build' to rebuild the solution" -ForegroundColor Gray