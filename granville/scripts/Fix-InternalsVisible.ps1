# Fix-InternalsVisible.ps1
# This script fixes InternalsVisibleTo attributes and accessibility issues after namespace conversion

param(
    [Parameter(Mandatory=$true)]
    [string]$RootPath,
    
    [Parameter()]
    [switch]$DryRun = $false
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

Write-Info "Scanning for InternalsVisibleTo attributes and accessibility issues..."

# First, let's find all AssemblyInfo.cs files and files with InternalsVisibleTo
$files = Get-ChildItem -Path $RootPath -Recurse -Include "*.cs", "*.csproj" | Where-Object {
    $content = Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue
    $content -match "InternalsVisibleTo|AssemblyInfo|ObserverGrainId"
}

Write-Info "Found $($files.Count) files to check"

foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw
    $originalContent = $content
    $changes = @()
    
    # Fix InternalsVisibleTo attributes
    if ($content -match "InternalsVisibleTo") {
        # Pattern to match InternalsVisibleTo with Orleans assemblies
        $pattern = '\[assembly:\s*InternalsVisibleTo\("Orleans([^"]*)"([^\]]*)\)\]'
        $matches = [regex]::Matches($content, $pattern)
        
        foreach ($match in $matches) {
            $fullMatch = $match.Value
            $assemblyNamePart = $match.Groups[1].Value
            $additionalPart = $match.Groups[2].Value
            
            # Create both Orleans and Forkleans versions
            $newAttribute = "[assembly: InternalsVisibleTo(`"Forkleans$assemblyNamePart`"$additionalPart)]"
            
            # Add the new attribute after the original
            $replacement = "$fullMatch`r`n$newAttribute"
            $content = $content.Replace($fullMatch, $replacement)
            
            $changes += "Added Forkleans version of InternalsVisibleTo for Orleans$assemblyNamePart"
        }
    }
    
    # Also check for the reverse - Forkleans assemblies might need to be visible to Orleans
    if ($content -match '\[assembly:\s*InternalsVisibleTo\("Forkleans') {
        # Check if we also need Orleans visibility
        $pattern = '\[assembly:\s*InternalsVisibleTo\("Forkleans([^"]*)"([^\]]*)\)\]'
        $matches = [regex]::Matches($content, $pattern)
        
        foreach ($match in $matches) {
            $assemblyNamePart = $match.Groups[1].Value
            $additionalPart = $match.Groups[2].Value
            
            # Check if Orleans version exists
            $orleansVersion = "[assembly: InternalsVisibleTo(`"Orleans$assemblyNamePart`"$additionalPart)]"
            if (-not ($content -match [regex]::Escape($orleansVersion))) {
                $fullMatch = $match.Value
                $replacement = "$fullMatch`r`n$orleansVersion"
                $content = $content.Replace($fullMatch, $replacement)
                
                $changes += "Added Orleans version of InternalsVisibleTo for Forkleans$assemblyNamePart"
            }
        }
    }
    
    # Check for specific type accessibility issues
    if ($file.FullName -match "ClientObserver\.cs") {
        Write-Info "Checking ClientObserver.cs for accessibility issues..."
        
        # Look for ObserverGrainId usage
        if ($content -match "ObserverGrainId") {
            Write-Info "Found ObserverGrainId usage in $($file.Name)"
            
            # Add a comment to track the issue
            if (-not ($content -match "// TODO: ObserverGrainId accessibility")) {
                $content = $content -replace "(.*ObserverGrainId.*)", '// TODO: ObserverGrainId accessibility issue - may need to make public or add InternalsVisibleTo`r`n$1'
                $changes += "Added TODO comment for ObserverGrainId accessibility"
            }
        }
    }
    
    # If we made changes, update the file
    if ($content -ne $originalContent) {
        Write-Change "Updating $($file.FullName.Substring($RootPath.Length + 1))"
        foreach ($change in $changes) {
            Write-Host "  - $change" -ForegroundColor Gray
        }
        
        if (-not $DryRun) {
            Set-Content -Path $file.FullName -Value $content -NoNewline
        }
    }
}

# Now let's scan for all internal types that might need to be exposed
Write-Info "`nScanning for internal types that might need exposure..."

$internalTypes = @{}
$potentialIssues = @()

Get-ChildItem -Path $RootPath -Recurse -Filter "*.cs" | ForEach-Object {
    $content = Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue
    if ($content) {
        # Find internal classes/types
        $matches = [regex]::Matches($content, 'internal\s+(class|struct|interface|enum)\s+(\w+)')
        foreach ($match in $matches) {
            $typeName = $match.Groups[2].Value
            if (-not $internalTypes.ContainsKey($typeName)) {
                $internalTypes[$typeName] = @()
            }
            $internalTypes[$typeName] += $_.FullName.Substring($RootPath.Length + 1)
        }
    }
}

# Check if ObserverGrainId is in the internal types
if ($internalTypes.ContainsKey("ObserverGrainId")) {
    Write-Info "`nFound ObserverGrainId defined as internal in:"
    foreach ($location in $internalTypes["ObserverGrainId"]) {
        Write-Host "  - $location" -ForegroundColor Yellow
    }
    
    $potentialIssues += @"
ObserverGrainId is defined as internal. Solutions:
1. Add InternalsVisibleTo attributes for your Forkleans assemblies
2. Change ObserverGrainId from 'internal' to 'public'
3. Create a public wrapper or interface
"@
}

# Generate a report
$reportPath = Join-Path $RootPath "internals-accessibility-report.txt"
$report = @"
InternalsVisibleTo and Accessibility Report
==========================================
Date: $(Get-Date)
Files Processed: $($files.Count)
Internal Types Found: $($internalTypes.Count)

Known Issues:
"@

if ($potentialIssues.Count -gt 0) {
    $report += "`n" + ($potentialIssues -join "`n`n")
} else {
    $report += "`nNo immediate issues found."
}

if ($internalTypes.Count -gt 10) {
    $report += "`n`nTop 10 Internal Types:"
    $internalTypes.GetEnumerator() | Select-Object -First 10 | ForEach-Object {
        $report += "`n- $($_.Key) (found in $($_.Value.Count) file(s))"
    }
}

$report | Set-Content -Path $reportPath
Write-Info "`nReport saved to: $reportPath"

# Specific fix for ObserverGrainId
Write-Host "`nTo fix the ObserverGrainId issue specifically:" -ForegroundColor Magenta
Write-Host "1. Look for the file where ObserverGrainId is defined"
Write-Host "2. Add this attribute to the assembly that contains it:"
Write-Host '   [assembly: InternalsVisibleTo("Forkleans.Core")]' -ForegroundColor Green
Write-Host "3. Or change 'internal class ObserverGrainId' to 'public class ObserverGrainId'"

if ($DryRun) {
    Write-Warning "`nThis was a dry run. No files were modified."
}
