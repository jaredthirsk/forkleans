#!/usr/bin/env pwsh
# Rename-Granville-To-Granville.ps1
# Carefully renames Granville to Granville with targeted replacements

param(
    [Parameter()]
    [switch]$DryRun = $false,
    
    [Parameter()]
    [switch]$SkipBackup = $false
)

$ErrorActionPreference = "Stop"

# Create backup if requested
if (-not $SkipBackup -and -not $DryRun) {
    Write-Host "Creating backup..." -ForegroundColor Yellow
    $backupDir = "backup_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
    git archive HEAD --format=tar | tar -xf - -C $backupDir
    Write-Host "Backup created in: $backupDir" -ForegroundColor Green
}

Write-Host "`nStarting careful rename from Granville to Granville..." -ForegroundColor Cyan

$totalFiles = 0
$modifiedFiles = 0

# Function to process files with specific patterns
function Process-Files {
    param(
        [string]$Pattern,
        [scriptblock]$ProcessContent,
        [string]$Description
    )
    
    Write-Host "`n$Description" -ForegroundColor Yellow
    
    $files = Get-ChildItem -Path . -Filter $Pattern -Recurse -File | 
             Where-Object { $_.FullName -notmatch '\\\.git\\|/\.git/' -and 
                           $_.FullName -notmatch '\\bin\\|/bin/' -and
                           $_.FullName -notmatch '\\obj\\|/obj/' -and
                           $_.FullName -notmatch '\\backup_|/backup_' }
    
    foreach ($file in $files) {
        $script:totalFiles++
        $content = Get-Content $file.FullName -Raw
        $newContent = & $ProcessContent $content
        
        if ($content -ne $newContent) {
            $script:modifiedFiles++
            Write-Host "  Modified: $($file.FullName)" -ForegroundColor Gray
            if (-not $DryRun) {
                Set-Content -Path $file.FullName -Value $newContent -NoNewline
            }
        }
    }
}

# 1. C# Source Files
Process-Files -Pattern "*.cs" -Description "Processing C# source files..." -ProcessContent {
    param($content)
    
    # Namespaces and using statements
    $content = $content -replace '\busing\s+Granville\b', 'using Granville'
    $content = $content -replace '\bnamespace\s+Granville\b', 'namespace Granville'
    
    # Qualified type names (preserve word boundaries)
    $content = $content -replace '\bForkleans\.', 'Granville.'
    
    # Type names starting with Granville
    $content = $content -replace '\bForkleans([A-Z][a-zA-Z0-9]*)\b', 'Granville$1'
    $content = $content -replace '\bIForkleans([A-Z][a-zA-Z0-9]*)\b', 'IGranville$1'
    
    # Alias attributes
    $content = $content -replace '"Granville\.', '"Granville.'
    
    return $content
}

# 2. Project Files
Process-Files -Pattern "*.csproj" -Description "Processing project files..." -ProcessContent {
    param($content)
    
    # Package references
    $content = $content -replace 'Include="Granville\.', 'Include="Granville.'
    
    # Assembly names
    $content = $content -replace '<AssemblyName>Granville\.', '<AssemblyName>Granville.'
    
    # Using statements in project files
    $content = $content -replace '<Using Include="Orleans" Alias="Granville"', '<Using Include="Orleans" Alias="Granville"'
    
    # Package IDs
    $content = $content -replace '<PackageId>Granville\.', '<PackageId>Granville.'
    
    return $content
}

# 3. F# Project Files
Process-Files -Pattern "*.fsproj" -Description "Processing F# project files..." -ProcessContent {
    param($content)
    
    # Same as C# project files
    $content = $content -replace 'Include="Granville\.', 'Include="Granville.'
    $content = $content -replace '<AssemblyName>Granville\.', '<AssemblyName>Granville.'
    $content = $content -replace '<Using Include="Orleans" Alias="Granville"', '<Using Include="Orleans" Alias="Granville"'
    $content = $content -replace '<PackageId>Granville\.', '<PackageId>Granville.'
    
    return $content
}

# 4. F# Source Files
Process-Files -Pattern "*.fs" -Description "Processing F# source files..." -ProcessContent {
    param($content)
    
    # F# namespaces and opens
    $content = $content -replace '\bopen\s+Granville\b', 'open Granville'
    $content = $content -replace '\bnamespace\s+Granville\b', 'namespace Granville'
    $content = $content -replace '\bForkleans\.', 'Granville.'
    
    return $content
}

# 5. Props and Targets Files
Process-Files -Pattern "*.props" -Description "Processing .props files..." -ProcessContent {
    param($content)
    
    # Property names that reference Granville
    $content = $content -replace '>Granville\.', '>Granville.'
    $content = $content -replace '"Granville\.', '"Granville.'
    $content = $content -replace 'Forkleans_', 'Granville_'
    
    return $content
}

Process-Files -Pattern "*.targets" -Description "Processing .targets files..." -ProcessContent {
    param($content)
    
    # Same as props files
    $content = $content -replace '>Granville\.', '>Granville.'
    $content = $content -replace '"Granville\.', '"Granville.'
    $content = $content -replace 'Include="Granville\.', 'Include="Granville.'
    
    return $content
}

# 6. PowerShell Scripts
Process-Files -Pattern "*.ps1" -Description "Processing PowerShell scripts..." -ProcessContent {
    param($content)
    
    # String literals and comments
    $content = $content -replace '\bForkleans\b', 'Granville'
    $content = $content -replace '\bforkleans\b', 'granville'
    
    # But preserve file paths that might exist
    if ($content -match 'Create-ForkleansPackages\.ps1') {
        # This file will be renamed separately
    }
    
    return $content
}

# 7. Markdown Files
Process-Files -Pattern "*.md" -Description "Processing Markdown files..." -ProcessContent {
    param($content)
    
    # General replacement in documentation
    $content = $content -replace '\bForkleans\b', 'Granville'
    $content = $content -replace '\bforkleans\b', 'granville'
    
    return $content
}

# 8. JSON Files
Process-Files -Pattern "*.json" -Description "Processing JSON files..." -ProcessContent {
    param($content)
    
    # Package names in JSON
    $content = $content -replace '"Granville\.', '"Granville.'
    $content = $content -replace '\bForkleans\b', 'Granville'
    
    return $content
}

# 9. XML Files (including NuGet.config)
Process-Files -Pattern "*.xml" -Description "Processing XML files..." -ProcessContent {
    param($content)
    
    $content = $content -replace '\bForkleans\b', 'Granville'
    $content = $content -replace '\bLocalForkleans\b', 'LocalGranville'
    
    return $content
}

Process-Files -Pattern "*.config" -Description "Processing config files..." -ProcessContent {
    param($content)
    
    $content = $content -replace '\bForkleans\b', 'Granville'
    $content = $content -replace '\bLocalForkleans\b', 'LocalGranville'
    
    return $content
}

# 10. Solution Files
Process-Files -Pattern "*.sln" -Description "Processing solution files..." -ProcessContent {
    param($content)
    
    # Only in comments or solution folder names
    $content = $content -replace '\bForkleans\b', 'Granville'
    
    return $content
}

# Now rename files
Write-Host "`nRenaming files..." -ForegroundColor Yellow

$filesToRename = @(
    @{ Old = "Create-ForkleansPackages.ps1"; New = "Create-GranvillePackages.ps1" },
    @{ Old = "Granville-version-bump.ps1"; New = "bump-granville-version.ps1" },
    @{ Old = "Granville-version-bump-guide.md"; New = "granville-version-bump-guide.md" }
)

foreach ($fileInfo in $filesToRename) {
    $oldPath = Join-Path . $fileInfo.Old
    $newPath = Join-Path . $fileInfo.New
    
    if (Test-Path $oldPath) {
        Write-Host "  Renaming: $($fileInfo.Old) -> $($fileInfo.New)" -ForegroundColor Gray
        if (-not $DryRun) {
            Move-Item -Path $oldPath -Destination $newPath -Force
        }
    }
}

# Find and rename files with Granville in the name
$filesWithForkleans = Get-ChildItem -Path . -Recurse -File | 
                     Where-Object { $_.Name -match 'Granville|Granville' -and 
                                   $_.FullName -notmatch '\\\.git\\|/\.git/' }

foreach ($file in $filesWithForkleans) {
    $newName = $file.Name -replace 'Granville', 'Granville' -replace 'Granville', 'granville'
    if ($newName -ne $file.Name) {
        $newPath = Join-Path $file.DirectoryName $newName
        Write-Host "  Renaming: $($file.Name) -> $newName" -ForegroundColor Gray
        if (-not $DryRun) {
            Move-Item -Path $file.FullName -Destination $newPath -Force
        }
    }
}

# Summary
Write-Host "`n========================================" -ForegroundColor Green
Write-Host "Rename Complete!" -ForegroundColor Green
Write-Host "Total files scanned: $totalFiles" -ForegroundColor Cyan
Write-Host "Files modified: $modifiedFiles" -ForegroundColor Cyan
Write-Host "Dry run: $DryRun" -ForegroundColor Cyan

if ($DryRun) {
    Write-Host "`nThis was a dry run. No files were actually modified." -ForegroundColor Yellow
    Write-Host "Run without -DryRun to apply changes." -ForegroundColor Yellow
}

Write-Host "`nNext steps:" -ForegroundColor Yellow
Write-Host "1. Review the changes with: git diff" -ForegroundColor Gray
Write-Host "2. Build the solution to verify: dotnet build" -ForegroundColor Gray
Write-Host "3. If issues arise, restore from backup or: git checkout -- ." -ForegroundColor Gray