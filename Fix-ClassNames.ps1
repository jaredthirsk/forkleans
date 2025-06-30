# Fix-ClassNames.ps1
# This script fixes class names that were incorrectly changed from Orleans to Forkleans
# Only namespaces and assembly names should change, not class names!

param(
    [Parameter(Mandatory=$true)]
    [string]$RootPath,

    [Parameter()]
    [switch]$DryRun = $false,

    [Parameter()]
    [switch]$CheckOnly = $false
)

$ErrorActionPreference = "Stop"

function Write-Info($message) {
    Write-Host "[INFO] $message" -ForegroundColor Cyan
}

function Write-Change($message) {
    Write-Host "[CHANGE] $message" -ForegroundColor Yellow
}

function Write-Success($message) {
    Write-Host "[SUCCESS] $message" -ForegroundColor Green
}

function Write-Error($message) {
    Write-Host "[ERROR] $message" -ForegroundColor Red
}

Write-Info "Scanning for incorrectly changed class names..."

# Patterns that likely indicate incorrectly changed class names
$suspiciousPatterns = @(
    # Class definitions
    'class\s+Forkleans\w+',
    'interface\s+IForkleans\w+',
    'struct\s+Forkleans\w+',
    'enum\s+Forkleans\w+',

    # Generic type parameters
    '<Forkleans[^\.>\s]+>',
    'IPostConfigureOptions<Forkleans[^>]+>',
    'IOptions<Forkleans[^>]+>',
    'IConfigureOptions<Forkleans[^>]+>',

    # Type references that aren't namespace qualified
    ':\s*Forkleans\w+',
    'new\s+Forkleans\w+',
    'typeof\(Forkleans\w+\)',

    # Method parameters
    '\(Forkleans\w+\s+\w+\)',

    # Attributes
    '\[Forkleans\w+\]',
    '\[Forkleans\w+\('
)

$issuesFound = @()
$filesFixed = 0

Get-ChildItem -Path $RootPath -Recurse -Filter "*.cs" | ForEach-Object {
    $file = $_
    $content = Get-Content $file.FullName -Raw
    $originalContent = $content
    $fileIssues = @()

    # Skip if file is in excluded directories
    if ($file.FullName -match '\\(bin|obj|\.git|\.vs)\\') {
        return
    }

    foreach ($pattern in $suspiciousPatterns) {
        $matches = [regex]::Matches($content, $pattern)

        foreach ($match in $matches) {
            $matchedText = $match.Value

            # Check if this is likely a class name (not a namespace)
            # Namespace references would typically be Forkleans.Something
            if ($matchedText -notmatch 'Forkleans\.' -and $matchedText -match 'Forkleans') {

                # Extract the likely class name
                if ($matchedText -match 'Forkleans(\w+)') {
                    $classNamePart = $matches[1]
                    $originalClassName = "Orleans$classNamePart"

                    # Some heuristics to confirm this is likely a class name
                    $isLikelyClassName = $false

                    # Check if it's a known Orleans type pattern
                    $knownOrleansTypes = @(
                        'ClientBuilder', 'SiloBuilder', 'GrainId', 'GrainReference',
                        'JsonSerializerOptions', 'SerializationManager', 'CodecProvider',
                        'CopierProvider', 'ConverterProvider', 'Options$', 'Configuration$',
                        'Settings$', 'Attribute$', 'Exception$', 'Host$', 'Client$',
                        'Silo$', 'Builder$', 'Factory$', 'Manager$', 'Provider$',
                        'Service$', 'Handler$', 'Context$', 'Request$', 'Response$'
                    )

                    foreach ($pattern in $knownOrleansTypes) {
                        if ($classNamePart -match $pattern) {
                            $isLikelyClassName = $true
                            break
                        }
                    }

                    # Also check if it follows PascalCase and doesn't look like a namespace
                    if ($classNamePart -match '^[A-Z][a-zA-Z0-9]*$' -and $classNamePart.Length -gt 3) {
                        $isLikelyClassName = $true
                    }

                    if ($isLikelyClassName) {
                        $fileIssues += [PSCustomObject]@{
                            Line = ($content.Substring(0, $match.Index) -split "`n").Count
                            Original = $matchedText
                            Fixed = $matchedText -replace 'Forkleans', 'Orleans'
                            Context = $match.Value
                        }
                    }
                }
            }
        }
    }

    if ($fileIssues.Count -gt 0) {
        $relativePath = $file.FullName.Substring($RootPath.Length + 1)
        Write-Change "Found issues in: $relativePath"

        $issuesFound += [PSCustomObject]@{
            File = $relativePath
            Issues = $fileIssues
        }

        foreach ($issue in $fileIssues) {
            Write-Host "  Line $($issue.Line): $($issue.Original) -> $($issue.Fixed)" -ForegroundColor Yellow
        }

        if (-not $CheckOnly -and -not $DryRun) {
            # Apply fixes
            foreach ($issue in $fileIssues | Sort-Object { $_.Original.Length } -Descending) {
                $content = $content -replace [regex]::Escape($issue.Original), $issue.Fixed
            }

            if ($content -ne $originalContent) {
                Set-Content -Path $file.FullName -Value $content -NoNewline
                $filesFixed++
                Write-Success "  Fixed class names"
            }
        }
    }
}

# Special check for the specific case you mentioned
Write-Info "`nChecking for specific known issues..."

$specificPatterns = @{
    'ForkleansJsonSerializerOptions' = 'OrleansJsonSerializerOptions'
    'ForkleansCodecProvider' = 'OrleansCodecProvider'
    'ForkleansCopierProvider' = 'OrleansCopierProvider'
    'ForkleansConverterProvider' = 'OrleansConverterProvider'
    'ForkleansGeneratedCodeHelper' = 'OrleansGeneratedCodeHelper'
    'ForkleansClientBuilder' = 'OrleansClientBuilder'
    'ForkleansSiloBuilder' = 'OrleansSiloBuilder'
}

foreach ($wrong in $specificPatterns.Keys) {
    $files = Get-ChildItem -Path $RootPath -Recurse -Filter "*.cs" | Where-Object {
        $content = Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue
        $content -match $wrong
    }

    if ($files) {
        Write-Change "`nFound specific issue: $wrong should be $($specificPatterns[$wrong])"
        foreach ($file in $files) {
            $relativePath = $file.FullName.Substring($RootPath.Length + 1)
            Write-Host "  In file: $relativePath" -ForegroundColor Yellow

            if (-not $CheckOnly -and -not $DryRun) {
                $content = Get-Content $file.FullName -Raw
                $content = $content -replace $wrong, $specificPatterns[$wrong]
                Set-Content -Path $file.FullName -Value $content -NoNewline
                Write-Success "  Fixed"
            }
        }
    }
}

# Summary
Write-Host "`n=== SUMMARY ===" -ForegroundColor Magenta

if ($issuesFound.Count -gt 0) {
    Write-Host "`nFiles with incorrectly changed class names: $($issuesFound.Count)" -ForegroundColor Yellow

    $totalIssues = ($issuesFound | ForEach-Object { $_.Issues.Count } | Measure-Object -Sum).Sum
    Write-Host "Total issues found: $totalIssues" -ForegroundColor Yellow

    if ($CheckOnly) {
        Write-Host "`nRun without -CheckOnly to fix these issues" -ForegroundColor Cyan
    } elseif ($DryRun) {
        Write-Host "`nThis was a dry run. Run without -DryRun to apply fixes" -ForegroundColor Cyan
    } else {
        Write-Success "`nFixed $filesFixed file(s)"
    }
} else {
    Write-Success "`nNo incorrectly changed class names found!"
}

# Generate detailed report
$reportPath = Join-Path $RootPath "class-names-fix-report.txt"
$report = @"
Class Names Fix Report
=====================
Date: $(Get-Date)
Files Scanned: $(Get-ChildItem -Path $RootPath -Recurse -Filter "*.cs" | Measure-Object).Count
Files with Issues: $($issuesFound.Count)
Files Fixed: $filesFixed

Guidelines:
- Only namespaces should change from Orleans to Forkleans
- Class names should remain unchanged
- Assembly names should change from Orleans to Forkleans
- Using statements should change: using Orleans -> using Forkleans

Issues Found:
"@

foreach ($fileIssue in $issuesFound) {
    $report += "`n`nFile: $($fileIssue.File)"
    foreach ($issue in $fileIssue.Issues) {
        $report += "`n  Line $($issue.Line): $($issue.Original) -> $($issue.Fixed)"
    }
}

$report | Set-Content -Path $reportPath
Write-Info "`nDetailed report saved to: $reportPath"

# Provide guidance
Write-Host "`nGuidance:" -ForegroundColor Magenta
Write-Host "- Class names like 'OrleansJsonSerializerOptions' should NOT change"
Write-Host "- Only the namespace should change: Orleans.Serialization -> Forkleans.Serialization"
Write-Host "- Assembly names change: Orleans.Core.dll -> Forkleans.Core.dll"
Write-Host "- Using statements change: using Orleans; -> using Forkleans;"
