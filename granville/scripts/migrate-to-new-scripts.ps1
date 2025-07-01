#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Migrates from old Granville build scripts to new streamlined scripts.

.DESCRIPTION
    This script helps transition from the old packaging approach (packaging Orleans + RPC)
    to the new approach (packaging only RPC, using official Microsoft.Orleans packages).

.PARAMETER Archive
    Archive the old scripts instead of deleting them
    Default: true

.PARAMETER DryRun
    Show what would be done without making changes
    Default: false
#>
param(
    [switch]$Archive = $true,
    [switch]$DryRun = $false
)

$ErrorActionPreference = "Stop"

Write-Host "Migrating to new streamlined build scripts..." -ForegroundColor Green

# Old scripts to handle
$oldScripts = @(
    "granville-version-bump.ps1",
    "Create-GranvillePackages.ps1"
)

# New scripts
$newScripts = @(
    "bump-granville-version.ps1",
    "build-granville-rpc.ps1"
)

if ($DryRun) {
    Write-Host "`nDRY RUN MODE - No changes will be made" -ForegroundColor Yellow
}

# Check if new scripts exist
Write-Host "`nChecking new scripts..." -ForegroundColor Cyan
foreach ($script in $newScripts) {
    if (Test-Path $script) {
        Write-Host "  ✓ $script exists" -ForegroundColor Green
    } else {
        Write-Host "  ✗ $script not found" -ForegroundColor Red
        exit 1
    }
}

# Handle old scripts
Write-Host "`nHandling old scripts..." -ForegroundColor Cyan
if ($Archive) {
    # Create archive directory
    $archiveDir = "archived-scripts"
    if (-not $DryRun -and -not (Test-Path $archiveDir)) {
        New-Item -ItemType Directory -Path $archiveDir | Out-Null
    }
    
    foreach ($script in $oldScripts) {
        if (Test-Path $script) {
            if ($DryRun) {
                Write-Host "  Would archive: $script -> $archiveDir/$script" -ForegroundColor Gray
            } else {
                Move-Item -Path $script -Destination "$archiveDir/$script" -Force
                Write-Host "  Archived: $script -> $archiveDir/$script" -ForegroundColor Yellow
            }
        }
    }
} else {
    foreach ($script in $oldScripts) {
        if (Test-Path $script) {
            if ($DryRun) {
                Write-Host "  Would delete: $script" -ForegroundColor Gray
            } else {
                Remove-Item -Path $script -Force
                Write-Host "  Deleted: $script" -ForegroundColor Yellow
            }
        }
    }
}

# Update CLAUDE.md if it exists
$claudeMdPath = "CLAUDE.md"
if (Test-Path $claudeMdPath) {
    Write-Host "`nUpdating CLAUDE.md..." -ForegroundColor Cyan
    if (-not $DryRun) {
        $content = Get-Content $claudeMdPath -Raw
        
        # Replace old script names with new ones
        $content = $content -replace './granville-version-bump\.ps1', './bump-granville-version.ps1'
        $content = $content -replace 'granville-version-bump\.ps1', 'bump-granville-version.ps1'
        $content = $content -replace './Create-GranvillePackages\.ps1', './build-granville-rpc.ps1'
        $content = $content -replace 'Create-GranvillePackages\.ps1', 'build-granville-rpc.ps1'
        
        Set-Content -Path $claudeMdPath -Value $content -NoNewline
        Write-Host "  ✓ Updated script references in CLAUDE.md" -ForegroundColor Green
    } else {
        Write-Host "  Would update script references in CLAUDE.md" -ForegroundColor Gray
    }
}

# Summary
Write-Host "`n=== Migration Summary ===" -ForegroundColor Cyan
Write-Host "Old scripts (granville-version-bump.ps1, Create-GranvillePackages.ps1):" -ForegroundColor Gray
if ($Archive) {
    Write-Host "  → Archived to $archiveDir/" -ForegroundColor Yellow
} else {
    Write-Host "  → Deleted" -ForegroundColor Yellow
}

Write-Host "`nNew streamlined scripts:" -ForegroundColor Gray
Write-Host "  • bump-granville-version.ps1 - Simple version bumping" -ForegroundColor Green
Write-Host "  • build-granville-rpc.ps1 - Build and package only RPC projects" -ForegroundColor Green

Write-Host "`nKey differences:" -ForegroundColor Yellow
Write-Host "  • New scripts only package Granville.Rpc.* packages" -ForegroundColor Gray
Write-Host "  • Orleans packages come from official Microsoft.Orleans.* on nuget.org" -ForegroundColor Gray
Write-Host "  • Simpler, faster, and more maintainable" -ForegroundColor Gray

Write-Host "`nUsage examples:" -ForegroundColor Cyan
Write-Host "  # Bump version and build packages:" -ForegroundColor Gray
Write-Host "  ./bump-granville-version.ps1" -ForegroundColor White
Write-Host "  ./build-granville-rpc.ps1" -ForegroundColor White
Write-Host ""
Write-Host "  # Just build packages without version bump:" -ForegroundColor Gray
Write-Host "  ./build-granville-rpc.ps1" -ForegroundColor White
Write-Host ""
Write-Host "  # Bump to specific version:" -ForegroundColor Gray
Write-Host "  ./bump-granville-version.ps1 -VersionPart Full -NewVersion 9.2.0.1" -ForegroundColor White

if ($DryRun) {
    Write-Host "`nTo perform the migration, run without -DryRun" -ForegroundColor Yellow
} else {
    Write-Host "`nMigration complete!" -ForegroundColor Green
}