# Fix missing using statements in files that use Orleans/Forkleans types
# This is a safer version that only deduplicates using statements, not the entire file

param(
    [string]$Path = ".",
    [switch]$DryRun = $false
)

$ErrorActionPreference = "Stop"

# Types that require Forkleans namespace
$forkleanTypes = @(
    'IGrain', 'Grain', 'IGrainFactory', 'IClusterClient', 'ISiloBuilder', 
    'IGrainWithIntegerKey', 'IGrainWithGuidKey', 'IGrainWithStringKey',
    'GrainFactory', 'IManagementGrain', 'RequestContext', 'SiloAddress',
    'GrainId', 'GenerateCodeForDeclaringAssembly', 'ImplicitStreamSubscription',
    'CollectionAgeLimit'
)

# Build regex pattern for type detection
$typePattern = ($forkleanTypes | ForEach-Object { [regex]::Escape($_) }) -join '|'

function Add-ForkleanUsing {
    param(
        [string]$FilePath,
        [string]$Content
    )
    
    # Check if already has using Forkleans
    if ($Content -match '^\s*using\s+Forkleans\s*;' -or $Content -match '^\s*open\s+Forkleans\s*$') {
        return $Content
    }
    
    # Check if file uses any Forkleans types
    if ($Content -notmatch "\b($typePattern)\b") {
        return $Content
    }
    
    # For C# files
    if ($FilePath -match '\.cs$') {
        # Find the position to insert the using statement
        if ($Content -match '(?m)^(\s*using\s+[^;]+;\s*\n)') {
            # Find the last using statement
            $lastUsingMatch = $Content | Select-String -Pattern '(?m)^(\s*using\s+[^;]+;\s*\n)' -AllMatches
            if ($lastUsingMatch.Matches.Count -gt 0) {
                $lastMatch = $lastUsingMatch.Matches[-1]
                $insertPos = $lastMatch.Index + $lastMatch.Length
                $Content = $Content.Insert($insertPos, "using Forkleans;`n")
            }
        } elseif ($Content -match '(?m)^(\s*namespace\s+)') {
            # Insert before namespace
            $Content = $Content -replace '(?m)^(\s*namespace\s+)', "using Forkleans;`n`n`$1"
        } else {
            # Insert at the beginning
            $Content = "using Forkleans;`n`n" + $Content
        }
    }
    # For F# files
    elseif ($FilePath -match '\.(fs|fsi|fsx)$') {
        # Find the position to insert the open statement
        if ($Content -match '(?m)^(\s*open\s+[^\n]+\n)') {
            # Find the last open statement
            $lastOpenMatch = $Content | Select-String -Pattern '(?m)^(\s*open\s+[^\n]+\n)' -AllMatches
            if ($lastOpenMatch.Matches.Count -gt 0) {
                $lastMatch = $lastOpenMatch.Matches[-1]
                $insertPos = $lastMatch.Index + $lastMatch.Length
                $Content = $Content.Insert($insertPos, "open Forkleans`n")
            }
        } elseif ($Content -match '(?m)^(\s*namespace\s+)') {
            # Insert after namespace
            $Content = $Content -replace '(?m)^(\s*namespace\s+[^\n]+\n)', "`$0`nopen Forkleans`n"
        } else {
            # Insert at the beginning
            $Content = "open Forkleans`n`n" + $Content
        }
    }
    
    return $Content
}

function Add-ForkleanHostingUsing {
    param(
        [string]$FilePath,
        [string]$Content
    )
    
    # Check if already has using Forkleans.Hosting
    if ($Content -match '^\s*using\s+Forkleans\.Hosting\s*;') {
        return $Content
    }
    
    # Check if file uses ISiloBuilder or extension methods
    if ($Content -notmatch '\b(ISiloBuilder|ISiloConfigurator|UseOrleans|Configure<.*Options>|AddActivationRebalancer|AddActivationRepartitioner)\b') {
        return $Content
    }
    
    # For C# files only
    if ($FilePath -match '\.cs$') {
        # Find the position to insert after using Forkleans
        if ($Content -match '(?m)^(\s*using\s+Forkleans\s*;\s*\n)') {
            $match = [regex]::Match($Content, '(?m)^(\s*using\s+Forkleans\s*;\s*\n)')
            $insertPos = $match.Index + $match.Length
            $Content = $Content.Insert($insertPos, "using Forkleans.Hosting;`n")
        }
    }
    
    return $Content
}

function Add-ForkleanRuntimeUsing {
    param(
        [string]$FilePath,
        [string]$Content
    )
    
    # Check if already has using Forkleans.Runtime
    if ($Content -match '^\s*using\s+Forkleans\.Runtime\s*;') {
        return $Content
    }
    
    # Check if file uses Runtime types
    if ($Content -notmatch '\b(RequestContext|IManagementGrain|SiloAddress|GrainId)\b') {
        return $Content
    }
    
    # For C# files only
    if ($FilePath -match '\.cs$') {
        # Find the position to insert after using Forkleans
        if ($Content -match '(?m)^(\s*using\s+Forkleans\s*;\s*\n)') {
            $match = [regex]::Match($Content, '(?m)^(\s*using\s+Forkleans\s*;\s*\n)')
            $insertPos = $match.Index + $match.Length
            $Content = $Content.Insert($insertPos, "using Forkleans.Runtime;`n")
        }
    }
    
    return $Content
}

function Remove-DuplicateUsings {
    param(
        [string]$Content
    )
    
    # Split content into lines
    $lines = $Content -split "`n"
    $processedLines = @()
    $seenUsings = @{}
    
    foreach ($line in $lines) {
        if ($line -match '^\s*using\s+([^;]+)\s*;') {
            $usingNamespace = $Matches[1].Trim()
            if (-not $seenUsings.ContainsKey($usingNamespace)) {
                $seenUsings[$usingNamespace] = $true
                $processedLines += $line
            }
            # Skip duplicate using statements
        } elseif ($line -match '^\s*open\s+([^\s]+)\s*$') {
            $openNamespace = $Matches[1].Trim()
            if (-not $seenUsings.ContainsKey($openNamespace)) {
                $seenUsings[$openNamespace] = $true
                $processedLines += $line
            }
            # Skip duplicate open statements
        } else {
            # Keep all other lines
            $processedLines += $line
        }
    }
    
    return $processedLines -join "`n"
}

Write-Host "Scanning for files with missing using statements..." -ForegroundColor Cyan

$files = Get-ChildItem -Path $Path -Recurse -Include "*.cs", "*.fs", "*.fsi", "*.fsx" | Where-Object {
    $_.FullName -notmatch '\\(bin|obj|\.git|artifacts|packages)\\' -and
    $_.Name -ne "AssemblyInfo.cs"
}

$fixedFiles = 0
$checkedFiles = 0

foreach ($file in $files) {
    $checkedFiles++
    $content = Get-Content $file.FullName -Raw
    if ([string]::IsNullOrWhiteSpace($content)) {
        continue
    }
    
    $originalContent = $content
    
    # Add missing using statements
    $content = Add-ForkleanUsing -FilePath $file.FullName -Content $content
    $content = Add-ForkleanHostingUsing -FilePath $file.FullName -Content $content
    $content = Add-ForkleanRuntimeUsing -FilePath $file.FullName -Content $content
    
    # Remove duplicate using/open statements (safely)
    $content = Remove-DuplicateUsings -Content $content
    
    if ($content -ne $originalContent) {
        $fixedFiles++
        Write-Host "Fixing: $($file.FullName)" -ForegroundColor Yellow
        
        if (-not $DryRun) {
            Set-Content -Path $file.FullName -Value $content -NoNewline
        }
    }
}

Write-Host "`nSummary:" -ForegroundColor Green
Write-Host "Files checked: $checkedFiles"
Write-Host "Files fixed: $fixedFiles"

if ($DryRun) {
    Write-Host "`nThis was a dry run. No files were actually modified." -ForegroundColor Cyan
}