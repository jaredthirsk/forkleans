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
$versionPrefix = ""
$granvilleRevision = ""

if ($content -match '<VersionPrefix[^>]*>([0-9]+\.[0-9]+\.[0-9]+)</VersionPrefix>') {
    $versionPrefix = $matches[1]
}
if ($content -match '<GranvilleRevision[^>]*>([0-9]+)</GranvilleRevision>') {
    $granvilleRevision = $matches[1]
}

if ($versionPrefix -and $granvilleRevision) {
    $currentVersion = "$versionPrefix.$granvilleRevision"
    Write-Host "Current version: $currentVersion" -ForegroundColor Cyan
} else {
    Write-Error "Could not find VersionPrefix and GranvilleRevision in Directory.Build.props"
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

# Update RPC Directory.Build.props
$rpcBuildPropsPath = Join-Path $PSScriptRoot "../../src/Rpc/Directory.Build.props"
if (Test-Path $rpcBuildPropsPath) {
    Write-Host "Updating RPC Directory.Build.props..." -ForegroundColor Yellow
    $rpcContent = Get-Content $rpcBuildPropsPath -Raw
    
    # Build the full version string for replacement
    if ($VersionPart -eq "Full") {
        $newVersionParts = $newVersionString.Split('.')
        $newPrefix = $newVersionParts[0..2] -join '.'
        $newRevision = $newVersionParts[3]
        
        # Update both VersionPrefix and GranvilleRevision
        $rpcContent = $rpcContent -replace '<VersionPrefix[^>]*>[0-9]+\.[0-9]+\.[0-9]+</VersionPrefix>', "<VersionPrefix Condition=`" '`$(VersionPrefix)'=='' `">$newPrefix</VersionPrefix>"
        $rpcContent = $rpcContent -replace '<GranvilleRevision[^>]*>[0-9]+</GranvilleRevision>', "<GranvilleRevision Condition=`" '`$(GranvilleRevision)'=='' `">$newRevision</GranvilleRevision>"
    } else {
        # Just update the revision
        $rpcContent = $rpcContent -replace '<GranvilleRevision[^>]*>[0-9]+</GranvilleRevision>', "<GranvilleRevision Condition=`" '`$(GranvilleRevision)'=='' `">$newRevision</GranvilleRevision>"
    }
    
    Set-Content -Path $rpcBuildPropsPath -Value $rpcContent -NoNewline
    Write-Host "Updated RPC Directory.Build.props" -ForegroundColor Green
} else {
    Write-Warning "RPC Directory.Build.props not found at: $rpcBuildPropsPath"
}

# Update all Directory.Packages.props files
Write-Host "Updating all Directory.Packages.props files..." -ForegroundColor Yellow
$packagePropsFiles = @(
    "../../Directory.Packages.props",
    "../samples/Rpc/Directory.Packages.props",
    "../compatibility-tools/codegen-discovery/Directory.Packages.props"
)

foreach ($relativePath in $packagePropsFiles) {
    $filePath = Join-Path $PSScriptRoot $relativePath
    if (Test-Path $filePath) {
        Write-Host "  Updating $(Split-Path $filePath -Leaf)..." -ForegroundColor Cyan
        $content = Get-Content $filePath -Raw
        
        # Build the full version string for replacement
        if ($VersionPart -eq "Full") {
            $fullVersion = $newVersionString
        } else {
            $fullVersion = "$versionPrefix.$newRevision"
        }
        
        # Update Granville.Rpc.* packages
        $content = $content -replace '(Granville\.Rpc\.[^"]+"\s+Version=")[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+(")', "`${1}$fullVersion`${2}"
        
        # Update Granville.Orleans.* packages
        $content = $content -replace '(Granville\.Orleans\.[^"]+"\s+Version=")[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+(")', "`${1}$fullVersion`${2}"
        
        # Update Microsoft.Orleans.* shim packages
        $content = $content -replace '(Microsoft\.Orleans\.[^"]+"\s+Version=")[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+-granville-shim(")', "`${1}$fullVersion-granville-shim`${2}"
        
        Set-Content -Path $filePath -Value $content -NoNewline
        Write-Host "  Updated $(Split-Path $filePath -Leaf)" -ForegroundColor Green
    } else {
        Write-Warning "  File not found: $filePath"
    }
}

# Update codegen-build project file
$codegenBuildPath = Join-Path $PSScriptRoot "../codegen-build/Orleans.Core.Abstractions.csproj"
if (Test-Path $codegenBuildPath) {
    Write-Host "Updating codegen-build project..." -ForegroundColor Yellow
    $content = Get-Content $codegenBuildPath -Raw
    
    # Build the full version string for replacement
    if ($VersionPart -eq "Full") {
        $fullVersion = $newVersionString
    } else {
        $fullVersion = "$versionPrefix.$newRevision"
    }
    
    # Update Granville.Orleans.* packages
    $content = $content -replace '(Granville\.Orleans\.[^"]+"\s+Version=")[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+(")', "`${1}$fullVersion`${2}"
    
    Set-Content -Path $codegenBuildPath -Value $content -NoNewline
    Write-Host "Updated codegen-build project" -ForegroundColor Green
} else {
    Write-Warning "Codegen-build project not found at: $codegenBuildPath"
}

Write-Host "`nVersion bump complete!" -ForegroundColor Green
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Clean old packages: Remove-Item ../../Artifacts/Release/*.nupkg -ErrorAction SilentlyContinue" -ForegroundColor Gray
Write-Host "  2. Build Orleans packages: ./build-granville-orleans-packages.ps1" -ForegroundColor Gray
Write-Host "  3. Build RPC packages: ./build-granville-rpc-packages.ps1" -ForegroundColor Gray
Write-Host "  4. Build shim packages: ./build-shims.ps1" -ForegroundColor Gray
Write-Host "  5. Test AppHost build: cd ../samples/Rpc/Shooter.AppHost && dotnet build" -ForegroundColor Gray
Write-Host "  6. Commit the version change" -ForegroundColor Gray