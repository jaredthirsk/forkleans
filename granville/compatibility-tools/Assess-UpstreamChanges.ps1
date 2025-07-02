#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Assesses changes from upstream Orleans in the Granville fork
.DESCRIPTION
    Analyzes the entire repository to identify added and modified files compared to upstream Orleans.
    Excludes Granville-specific folders (granville/ and src/Rpc) from the report.
#>

param(
    [string]$OutputPath = "./upstream-changes-report.md"
)

$ErrorActionPreference = "Stop"

# Get repository root
$repoRoot = git rev-parse --show-toplevel
if (!$repoRoot) {
    Write-Error "Not in a git repository"
    exit 1
}

Set-Location $repoRoot

Write-Host "Analyzing changes from upstream Orleans..." -ForegroundColor Cyan

# Initialize collections
$addedFiles = @()
$modifiedFiles = @()
$deletedFiles = @()

# Get the latest common ancestor with upstream
Write-Host "Finding merge base with upstream..." -ForegroundColor Yellow

# First, ensure we have the upstream remote
$upstreamRemote = git remote | Where-Object { $_ -eq "upstream" }
if (!$upstreamRemote) {
    Write-Host "No 'upstream' remote found. Adding Microsoft Orleans as upstream..." -ForegroundColor Yellow
    git remote add upstream https://github.com/dotnet/orleans.git
}

# Fetch upstream
Write-Host "Fetching upstream..." -ForegroundColor Yellow
git fetch upstream main --quiet

# Find merge base
$mergeBase = git merge-base HEAD upstream/main
if (!$mergeBase) {
    Write-Error "Could not find merge base with upstream/main"
    exit 1
}

Write-Host "Merge base: $mergeBase" -ForegroundColor Green

# Initialize collections for different areas
$srcAddedFiles = @()
$srcModifiedFiles = @()
$srcDeletedFiles = @()
$rootAddedFiles = @()
$rootModifiedFiles = @()
$rootDeletedFiles = @()
$otherAddedFiles = @()
$otherModifiedFiles = @()
$otherDeletedFiles = @()

# Get all changes in the repository
Write-Host "Analyzing changes across the entire repository..." -ForegroundColor Yellow

# Get file status compared to merge base, excluding Granville-specific folders and build artifacts
$gitDiff = git diff --name-status $mergeBase HEAD | Where-Object { 
    $_ -notmatch "^[AMD]\s+granville/" -and 
    $_ -notmatch "^[AMD]\s+src/Rpc/" -and 
    $_ -notmatch "^[AMD]\s+test/Rpc/" -and 
    $_ -notmatch "(bin/|obj/|artifacts/)" -and
    $_ -notmatch "\.git/"
}

foreach ($line in $gitDiff) {
    if ($line -match "^([AMD])\s+(.+)$") {
        $status = $Matches[1]
        $file = $Matches[2]
        
        # Categorize files by location
        if ($file -match "^src/") {
            switch ($status) {
                "A" { $srcAddedFiles += $file }
                "M" { $srcModifiedFiles += $file }
                "D" { $srcDeletedFiles += $file }
            }
        }
        elseif ($file -match "^[^/]+$") {
            # Root level files
            switch ($status) {
                "A" { $rootAddedFiles += $file }
                "M" { $rootModifiedFiles += $file }
                "D" { $rootDeletedFiles += $file }
            }
        }
        else {
            # Other directories (test/, samples/, etc.)
            switch ($status) {
                "A" { $otherAddedFiles += $file }
                "M" { $otherModifiedFiles += $file }
                "D" { $otherDeletedFiles += $file }
            }
        }
    }
}

# Also check for Granville-specific files across the entire repo
$granvillePatterns = @(
    "*Granville*",
    "*granville*",
    "*CLAUDE*"
)

foreach ($pattern in $granvillePatterns) {
    # Search in different areas
    $areas = @("src", "test", "samples", ".")
    
    foreach ($area in $areas) {
        if (Test-Path $area) {
            $searchPath = if ($area -eq ".") { Get-Location } else { $area }
            
            if ($area -eq ".") {
                # For root directory, don't recurse
                $granvilleFiles = Get-ChildItem -Path $searchPath -File -Filter $pattern -ErrorAction SilentlyContinue -Depth 0
            } else {
                # For other directories, recurse fully
                $granvilleFiles = Get-ChildItem -Path $searchPath -File -Filter $pattern -ErrorAction SilentlyContinue -Recurse
            }
            
            $granvilleFiles = $granvilleFiles | Where-Object { 
                    $_.FullName -notmatch "granville[/\\]" -and
                    $_.FullName -notmatch "src[/\\]Rpc[/\\]" -and
                    $_.FullName -notmatch "test[/\\]Rpc[/\\]" -and
                    $_.FullName -notmatch "[/\\](bin|obj|artifacts)[/\\]" -and
                    $_.FullName -notmatch "\.git[/\\]"
                } |
                ForEach-Object { $_.FullName.Replace($repoRoot + [System.IO.Path]::DirectorySeparatorChar, "").Replace("\", "/") }
            
            foreach ($file in $granvilleFiles) {
                # Check if this file exists in upstream
                $existsInUpstream = git cat-file -e "${mergeBase}:${file}" 2>$null
                if ($LASTEXITCODE -ne 0) {
                    # File doesn't exist in upstream, add to appropriate category
                    if ($file -match "^src/" -and $file -notin $srcAddedFiles) {
                        $srcAddedFiles += $file
                    }
                    elseif ($file -match "^[^/]+$" -and $file -notin $rootAddedFiles) {
                        $rootAddedFiles += $file
                    }
                    elseif ($file -notin $otherAddedFiles -and $file -notmatch "^src/" -and $file -notmatch "^[^/]+$") {
                        # Only add to other if it's not already in src/ or root
                        $otherAddedFiles += $file
                    }
                }
            }
        }
    }
}

# Sort all arrays
$srcAddedFiles = $srcAddedFiles | Sort-Object -Unique
$srcModifiedFiles = $srcModifiedFiles | Sort-Object -Unique
$srcDeletedFiles = $srcDeletedFiles | Sort-Object -Unique
$rootAddedFiles = $rootAddedFiles | Sort-Object -Unique
$rootModifiedFiles = $rootModifiedFiles | Sort-Object -Unique
$rootDeletedFiles = $rootDeletedFiles | Sort-Object -Unique
$otherAddedFiles = $otherAddedFiles | Sort-Object -Unique
$otherModifiedFiles = $otherModifiedFiles | Sort-Object -Unique
$otherDeletedFiles = $otherDeletedFiles | Sort-Object -Unique

# Calculate totals
$totalAdded = $srcAddedFiles.Count + $rootAddedFiles.Count + $otherAddedFiles.Count
$totalModified = $srcModifiedFiles.Count + $rootModifiedFiles.Count + $otherModifiedFiles.Count
$totalDeleted = $srcDeletedFiles.Count + $rootDeletedFiles.Count + $otherDeletedFiles.Count

# Generate report
Write-Host "Generating report..." -ForegroundColor Yellow

$report = @"
# Upstream Changes Assessment Report

Generated on: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Compared against upstream merge base: $mergeBase

## Summary

**Total changes (excluding granville/ and src/Rpc/):**
- **Added files**: $totalAdded
- **Modified files**: $totalModified
- **Deleted files**: $totalDeleted

### By Location:
- **src/ folder** (excluding src/Rpc/): Added: $($srcAddedFiles.Count), Modified: $($srcModifiedFiles.Count), Deleted: $($srcDeletedFiles.Count)
- **Root directory**: Added: $($rootAddedFiles.Count), Modified: $($rootModifiedFiles.Count), Deleted: $($rootDeletedFiles.Count)
- **Other directories**: Added: $($otherAddedFiles.Count), Modified: $($otherModifiedFiles.Count), Deleted: $($otherDeletedFiles.Count)

"@

# src/ folder changes
$report += @"
## Changes in src/ folder (excluding src/Rpc/)

"@

if ($srcAddedFiles.Count -gt 0) {
    $report += @"
### Added Files in src/

These files are new in the Granville fork and do not exist in upstream Orleans:

"@
    foreach ($file in $srcAddedFiles) {
        # Try to determine the purpose of the file
        $purpose = ""
        if ($file -match "AssemblyInfo\.Granville\.cs$") {
            $purpose = " - Granville-specific InternalsVisibleTo declarations"
        }
        $report += "- ``$file``$purpose`n"
    }
} else {
    $report += "No files added in src/ folder.`n`n"
}

if ($srcModifiedFiles.Count -gt 0) {
    $report += @"

### Modified Files in src/

These files have been modified from their upstream versions:

"@
    foreach ($file in $srcModifiedFiles) {
        $report += "- ``$file```n"
        
        # Get basic diff statistics
        $diffStat = git diff --stat $mergeBase HEAD -- $file 2>$null
        if ($diffStat -match "(\d+) insertion.*(\d+) deletion") {
            $insertions = $Matches[1]
            $deletions = $Matches[2]
            $report += "  - $insertions insertions, $deletions deletions`n"
        }
    }
} else {
    $report += "No files modified in src/ folder.`n`n"
}

if ($srcDeletedFiles.Count -gt 0) {
    $report += @"

### Deleted Files in src/

These files exist in upstream but have been removed in the Granville fork:

"@
    foreach ($file in $srcDeletedFiles) {
        $report += "- ``$file```n"
    }
}

# Root directory changes
$report += @"

## Changes in Root Directory

"@

if ($rootAddedFiles.Count -gt 0) {
    $report += @"
### Added Files in Root

"@
    foreach ($file in $rootAddedFiles) {
        $purpose = ""
        if ($file -eq "CLAUDE.md") {
            $purpose = " - Guidance for Claude Code AI assistant"
        } elseif ($file -match "Directory\.Build\.(props|targets)") {
            $purpose = " - MSBuild configuration for assembly renaming"
        }
        $report += "- ``$file``$purpose`n"
    }
}

if ($rootModifiedFiles.Count -gt 0) {
    $report += @"

### Modified Files in Root

"@
    foreach ($file in $rootModifiedFiles) {
        $report += "- ``$file```n"
        
        # Get basic diff statistics
        $diffStat = git diff --stat $mergeBase HEAD -- $file 2>$null
        if ($diffStat -match "(\d+) insertion.*(\d+) deletion") {
            $insertions = $Matches[1]
            $deletions = $Matches[2]
            $report += "  - $insertions insertions, $deletions deletions`n"
        }
    }
}

if ($rootDeletedFiles.Count -gt 0) {
    $report += @"

### Deleted Files in Root

"@
    foreach ($file in $rootDeletedFiles) {
        $report += "- ``$file```n"
    }
}

# Other directories
if ($otherAddedFiles.Count -gt 0 -or $otherModifiedFiles.Count -gt 0 -or $otherDeletedFiles.Count -gt 0) {
    $report += @"

## Changes in Other Directories (test/, samples/, etc.)

"@
    
    if ($otherAddedFiles.Count -gt 0) {
        $report += @"
### Added Files

"@
        foreach ($file in $otherAddedFiles) {
            $report += "- ``$file```n"
        }
    }
    
    if ($otherModifiedFiles.Count -gt 0) {
        $report += @"

### Modified Files

"@
        foreach ($file in $otherModifiedFiles) {
            $report += "- ``$file```n"
        }
    }
    
    if ($otherDeletedFiles.Count -gt 0) {
        $report += @"

### Deleted Files

"@
        foreach ($file in $otherDeletedFiles) {
            $report += "- ``$file```n"
        }
    }
}

# Analyze specific types of changes
$report += @"

## Analysis by File Type

### Assembly Info Files
"@

$allAddedFiles = $srcAddedFiles + $rootAddedFiles + $otherAddedFiles
$allModifiedFiles = $srcModifiedFiles + $rootModifiedFiles + $otherModifiedFiles
$assemblyInfoFiles = ($allAddedFiles + $allModifiedFiles) | Where-Object { $_ -match "AssemblyInfo.*\.cs$" } | Sort-Object -Unique

if ($assemblyInfoFiles.Count -gt 0) {
    foreach ($file in $assemblyInfoFiles) {
        $isGranville = $file -match "Granville"
        $status = if ($file -in $allAddedFiles) { "Added" } else { "Modified" }
        $report += "- ``$file`` ($status)"
        if ($isGranville) {
            $report += " - Granville-specific assembly attributes"
        }
        $report += "`n"
    }
} else {
    $report += "No assembly info files were added or modified.`n"
}

$report += @"

### Build Configuration Files
"@

$buildFiles = ($allAddedFiles + $allModifiedFiles) | Where-Object { $_ -match "(Directory\.Build\.|\.props$|\.targets$|\.csproj$)" } | Sort-Object -Unique
if ($buildFiles.Count -gt 0) {
    foreach ($file in $buildFiles) {
        $status = if ($file -in $allAddedFiles) { "Added" } else { "Modified" }
        $report += "- ``$file`` ($status)`n"
    }
} else {
    $report += "No build configuration files were added or modified.`n"
}

# Add section about excluded directories
$report += @"

## Excluded from Analysis

The following directories were excluded from this analysis as they are Granville-specific:
- `granville/` - All Granville-specific tools, scripts, and documentation
- `src/Rpc/` - Granville RPC implementation (hoped for upstream contribution)
- `test/Rpc/` - Tests for Granville RPC implementation

To see the contents of these directories, explore them directly in the repository.
"@

# Write report to file
$report | Out-File -FilePath $OutputPath -Encoding utf8

Write-Host "Report generated: $OutputPath" -ForegroundColor Green

# Also output to console
Write-Host "`n$report"

# Return summary for script usage
return @{
    TotalAddedCount = $totalAdded
    TotalModifiedCount = $totalModified
    TotalDeletedCount = $totalDeleted
    SrcAddedCount = $srcAddedFiles.Count
    SrcModifiedCount = $srcModifiedFiles.Count
    SrcDeletedCount = $srcDeletedFiles.Count
    RootAddedCount = $rootAddedFiles.Count
    RootModifiedCount = $rootModifiedFiles.Count
    RootDeletedCount = $rootDeletedFiles.Count
    OtherAddedCount = $otherAddedFiles.Count
    OtherModifiedCount = $otherModifiedFiles.Count
    OtherDeletedCount = $otherDeletedFiles.Count
    SrcAddedFiles = $srcAddedFiles
    SrcModifiedFiles = $srcModifiedFiles
    SrcDeletedFiles = $srcDeletedFiles
    RootAddedFiles = $rootAddedFiles
    RootModifiedFiles = $rootModifiedFiles
    RootDeletedFiles = $rootDeletedFiles
    OtherAddedFiles = $otherAddedFiles
    OtherModifiedFiles = $otherModifiedFiles
    OtherDeletedFiles = $otherDeletedFiles
    ReportPath = $OutputPath
}