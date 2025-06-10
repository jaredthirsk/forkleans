# Fix-AssemblyNames.ps1
# This script ensures assembly names match the namespace changes

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

Write-Info "Checking assembly names in project files..."

$issuesFound = @()
$projectsFixed = 0

Get-ChildItem -Path $RootPath -Recurse -Filter "*.csproj" | ForEach-Object {
    $projectFile = $_
    $projectName = $_.BaseName
    $content = Get-Content $_.FullName -Raw
    $originalContent = $content
    
    Write-Info "Checking: $projectName"
    
    # Expected assembly name (if it starts with Orleans, it should be Forkleans)
    $expectedAssemblyName = $projectName -replace "^Orleans", "Forkleans"
    
    # Check if this is an Orleans project that should be renamed
    if ($projectName -match "^Orleans" -and $expectedAssemblyName -match "^Forkleans") {
        
        # Look for AssemblyName tag
        if ($content -match '<AssemblyName>([^<]+)</AssemblyName>') {
            $currentAssemblyName = $matches[1]
            
            if ($currentAssemblyName -ne $expectedAssemblyName) {
                Write-Change "  Assembly name mismatch:"
                Write-Host "    Current:  $currentAssemblyName" -ForegroundColor Red
                Write-Host "    Expected: $expectedAssemblyName" -ForegroundColor Green
                
                $issuesFound += [PSCustomObject]@{
                    Project = $projectName
                    CurrentAssembly = $currentAssemblyName
                    ExpectedAssembly = $expectedAssemblyName
                    File = $_.FullName.Substring($RootPath.Length + 1)
                }
                
                if (-not $CheckOnly -and -not $DryRun) {
                    # Fix the assembly name
                    $content = $content -replace "<AssemblyName>$([regex]::Escape($currentAssemblyName))</AssemblyName>", "<AssemblyName>$expectedAssemblyName</AssemblyName>"
                    $projectsFixed++
                }
            } else {
                Write-Host "  ✓ Assembly name is correct: $currentAssemblyName" -ForegroundColor Green
            }
        } else {
            # No explicit AssemblyName - it defaults to project name
            if ($projectName -match "^Orleans") {
                Write-Change "  No AssemblyName tag found - adding one"
                
                $issuesFound += [PSCustomObject]@{
                    Project = $projectName
                    CurrentAssembly = "$projectName (default)"
                    ExpectedAssembly = $expectedAssemblyName
                    File = $_.FullName.Substring($RootPath.Length + 1)
                }
                
                if (-not $CheckOnly -and -not $DryRun) {
                    # Add AssemblyName after the first PropertyGroup opening
                    $content = $content -replace '(<PropertyGroup[^>]*>)', "`$1`n    <AssemblyName>$expectedAssemblyName</AssemblyName>"
                    $projectsFixed++
                }
            }
        }
        
        # Also check RootNamespace
        if ($content -match '<RootNamespace>([^<]+)</RootNamespace>') {
            $currentRootNamespace = $matches[1]
            $expectedRootNamespace = $currentRootNamespace -replace "^Orleans", "Forkleans"
            
            if ($currentRootNamespace -ne $expectedRootNamespace -and $currentRootNamespace -match "^Orleans") {
                Write-Change "  RootNamespace needs update: $currentRootNamespace -> $expectedRootNamespace"
                
                if (-not $CheckOnly -and -not $DryRun) {
                    $content = $content -replace "<RootNamespace>$([regex]::Escape($currentRootNamespace))</RootNamespace>", "<RootNamespace>$expectedRootNamespace</RootNamespace>"
                }
            }
        }
        
        # Update the file if we made changes
        if (-not $CheckOnly -and -not $DryRun -and $content -ne $originalContent) {
            Set-Content -Path $_.FullName -Value $content -NoNewline
            Write-Success "  Updated project file"
        }
    }
}

# Summary report
Write-Host "`n=== SUMMARY ===" -ForegroundColor Magenta

if ($issuesFound.Count -gt 0) {
    Write-Host "`nProjects with incorrect assembly names:" -ForegroundColor Yellow
    $issuesFound | Format-Table -Property Project, CurrentAssembly, ExpectedAssembly -AutoSize
    
    if ($CheckOnly) {
        Write-Host "`nRun without -CheckOnly to fix these issues" -ForegroundColor Cyan
    } elseif ($DryRun) {
        Write-Host "`nThis was a dry run. Run without -DryRun to apply fixes" -ForegroundColor Cyan
    } else {
        Write-Success "`nFixed $projectsFixed project file(s)"
    }
} else {
    Write-Success "`nAll assembly names are correct!"
}

# Additional checks for InternalsVisibleTo
Write-Host "`n=== Checking InternalsVisibleTo References ===" -ForegroundColor Magenta

$ivtIssues = @()

Get-ChildItem -Path $RootPath -Recurse -Include "*.cs", "*.csproj" | Where-Object {
    $content = Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue
    $content -match "InternalsVisibleTo"
} | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    $file = $_.FullName.Substring($RootPath.Length + 1)
    
    # Find all InternalsVisibleTo attributes
    $matches = [regex]::Matches($content, '\[assembly:\s*InternalsVisibleTo\("([^"]+)"\)')
    
    foreach ($match in $matches) {
        $referencedAssembly = $match.Groups[1].Value
        
        # Check if it references Forkleans assemblies
        if ($referencedAssembly -match "^Forkleans") {
            # Check if there's a corresponding project
            $expectedProject = $referencedAssembly + ".csproj"
            $projectExists = Get-ChildItem -Path $RootPath -Recurse -Filter $expectedProject | Select-Object -First 1
            
            if (-not $projectExists) {
                # Maybe it's still named Orleans?
                $orleansName = $referencedAssembly -replace "^Forkleans", "Orleans"
                $orleansProject = Get-ChildItem -Path $RootPath -Recurse -Filter "$orleansName.csproj" | Select-Object -First 1
                
                if ($orleansProject) {
                    $ivtIssues += [PSCustomObject]@{
                        File = $file
                        InternalsVisibleTo = $referencedAssembly
                        Issue = "Project still named $orleansName"
                        ProjectFile = $orleansProject.FullName.Substring($RootPath.Length + 1)
                    }
                }
            }
        }
    }
}

if ($ivtIssues.Count -gt 0) {
    Write-Host "`nInternalsVisibleTo mismatches found:" -ForegroundColor Yellow
    $ivtIssues | Format-Table -Property File, InternalsVisibleTo, Issue -AutoSize
}

# Save report
$reportPath = Join-Path $RootPath "assembly-names-report.txt"
@"
Assembly Names Report
====================
Date: $(Get-Date)
Projects Checked: $(Get-ChildItem -Path $RootPath -Recurse -Filter "*.csproj" | Measure-Object).Count
Issues Found: $($issuesFound.Count)
Projects Fixed: $projectsFixed

Assembly Name Mismatches:
$($issuesFound | ForEach-Object { "- $($_.Project): $($_.CurrentAssembly) -> $($_.ExpectedAssembly)" } | Out-String)

InternalsVisibleTo Issues:
$($ivtIssues | ForEach-Object { "- $($_.File): References $($_.InternalsVisibleTo) but project is $($_.Issue)" } | Out-String)
"@ | Set-Content -Path $reportPath

Write-Info "`nReport saved to: $reportPath"
