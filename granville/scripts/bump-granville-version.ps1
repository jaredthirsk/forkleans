#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Bumps the Granville.Rpc version number.

.DESCRIPTION
    This script updates the version number in Directory.Build.props for Granville.Rpc packages.
    The version format is Major.Minor.Patch.Revision where:
    - Major.Minor.Patch should match the Orleans version we're based on (e.g., 9.1.2)
    - Revision is our fork-specific version number

.PARAMETER VersionPart
    Which part to bump: Revision (default) or Full
    - Revision: Increments the last number (e.g., 9.1.2.50 -> 9.1.2.51)
    - Full: Set a complete version with -NewVersion parameter

.PARAMETER NewVersion
    When using -VersionPart Full, specify the complete version (e.g., "9.1.2.51")

.PARAMETER DryRun
    Show what would be changed without making changes
    Default: false

.EXAMPLE
    ./bump-granville-version.ps1
    Bumps revision from 9.1.2.50 to 9.1.2.51

.EXAMPLE
    ./bump-granville-version.ps1 -VersionPart Full -NewVersion 9.2.0.1
    Sets version to 9.2.0.1 (for when updating to a new Orleans base version)
#>
param(
    [ValidateSet("Revision", "Full")]
    [string]$VersionPart = "Revision",
    [string]$NewVersion = "",
    [switch]$DryRun = $false
)

$ErrorActionPreference = "Stop"

$buildPropsPath = Join-Path $PSScriptRoot "../../Directory.Build.props"

if (-not (Test-Path $buildPropsPath)) {
    Write-Error "Directory.Build.props not found at: $buildPropsPath"
    exit 1
}

# Read current version
$content = Get-Content $buildPropsPath -Raw
if ($content -match '<Version>([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)</Version>') {
    $currentVersion = $matches[1]
    Write-Host "Current version: $currentVersion" -ForegroundColor Cyan
} else {
    Write-Error "Could not find version in Directory.Build.props"
    exit 1
}

# Calculate new version
if ($VersionPart -eq "Full") {
    if (-not $NewVersion) {
        Write-Error "NewVersion parameter is required when using -VersionPart Full"
        exit 1
    }
    if ($NewVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$') {
        Write-Error "NewVersion must be in format Major.Minor.Patch.Revision (e.g., 9.1.2.51)"
        exit 1
    }
    $newVersionString = $NewVersion
} else {
    # Bump revision
    $versionParts = $currentVersion.Split('.')
    $versionParts[3] = [string]([int]$versionParts[3] + 1)
    $newVersionString = $versionParts -join '.'
}

Write-Host "New version: $newVersionString" -ForegroundColor Green

if ($DryRun) {
    Write-Host "DRY RUN - No changes made" -ForegroundColor Yellow
    exit 0
}

# Update Directory.Build.props
if ($VersionPart -eq "Full") {
    # Parse the new version
    $newVersionParts = $newVersionString.Split('.')
    $newPrefix = $newVersionParts[0..2] -join '.'
    $newRevision = $newVersionParts[3]
    
    # Update both VersionPrefix and GranvilleRevision
    $newContent = $content -replace '<VersionPrefix[^>]*>[0-9]+\.[0-9]+\.[0-9]+</VersionPrefix>', "<VersionPrefix Condition=`" '`$(VersionPrefix)'=='' `">$newPrefix</VersionPrefix>"
    $newContent = $newContent -replace '<GranvilleRevision[^>]*>[0-9]+</GranvilleRevision>', "<GranvilleRevision Condition=`" '`$(GranvilleRevision)'=='' `">$newRevision</GranvilleRevision>"
} else {
    # Just update the revision
    $newRevision = $versionParts[3]
    $newContent = $content -replace '<GranvilleRevision[^>]*>[0-9]+</GranvilleRevision>', "<GranvilleRevision Condition=`" '`$(GranvilleRevision)'=='' `">$newRevision</GranvilleRevision>"
}
Set-Content -Path $buildPropsPath -Value $newContent -NoNewline

Write-Host "Updated Directory.Build.props" -ForegroundColor Green

# Update current-revision.txt
$revisionFilePath = Join-Path $PSScriptRoot "../current-revision.txt"
Set-Content -Path $revisionFilePath -Value $newRevision -NoNewline
Write-Host "Updated current-revision.txt to: $newRevision" -ForegroundColor Green

Write-Host "`nVersion bump complete!" -ForegroundColor Green
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Build and package: ./build-granville-rpc.ps1" -ForegroundColor Gray
Write-Host "  2. Test the packages" -ForegroundColor Gray
Write-Host "  3. Commit the version change" -ForegroundColor Gray