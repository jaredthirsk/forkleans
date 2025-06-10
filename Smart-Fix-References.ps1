# Smart-Fix-References.ps1
# This script intelligently fixes project references after namespace conversion
# It handles the case where namespaces have changed but file/folder names haven't

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

function Write-Warning($message) {
    Write-Host "[WARNING] $message" -ForegroundColor Magenta
}

function Write-VerboseMessage($message) {
    Write-Verbose "[VERBOSE] $message"
}

# Normalize path for comparison
function Normalize-Path($path, $basePath) {
    if ([string]::IsNullOrWhiteSpace($path)) { return $null }

    # Handle MSBuild variables
    $expandedPath = $path
    $expandedPath = $expandedPath -replace '\$\(SourceRoot\)', "$RootPath\"
    $expandedPath = $expandedPath -replace '\$\(MSBuildThisFileDirectory\)', "$basePath\"

    # Make absolute path
    if (-not [System.IO.Path]::IsPathRooted($expandedPath)) {
        $expandedPath = Join-Path $basePath $expandedPath
    }

    try {
        return [System.IO.Path]::GetFullPath($expandedPath)
    }
    catch {
        return $null
    }
}

# Build a map of all available projects
Write-Info "Building project inventory..."
$projectMap = @{}
$projectsByName = @{}

Get-ChildItem -Path $RootPath -Recurse -Filter "*.csproj" | ForEach-Object {
    $projectPath = $_.FullName
    $projectName = $_.BaseName

    # Store by full path
    $projectMap[$projectPath] = $projectName

    # Store by name (might have duplicates)
    if (-not $projectsByName.ContainsKey($projectName)) {
        $projectsByName[$projectName] = @()
    }
    $projectsByName[$projectName] += $projectPath

    Write-VerboseMessage "Found project: $projectName at $($_.Directory.Name)"
}

Write-Info "Found $($projectMap.Count) projects"

# Function to find the best matching project
function Find-BestMatch($referencePath, $fromPath) {
    # Extract just the project name from the reference
    $refProjectName = [System.IO.Path]::GetFileNameWithoutExtension($referencePath)

    Write-VerboseMessage "Looking for best match for: $refProjectName"

    # Try different name variations
    $namesToTry = @(
        $refProjectName,
        ($refProjectName -replace "^Forkleans", "Orleans"),
        ($refProjectName -replace "^Orleans", "Forkleans")
    )

    foreach ($name in $namesToTry) {
        if ($projectsByName.ContainsKey($name)) {
            $candidates = $projectsByName[$name]

            if ($candidates.Count -eq 1) {
                Write-VerboseMessage "Found exact match by name: $name"
                return $candidates[0]
            }

            # Multiple candidates - try to find the best one based on relative path
            foreach ($candidate in $candidates) {
                # Check if the relative path structure matches
                $candidateRel = [System.IO.Path]::GetRelativePath($fromPath, $candidate)
                $originalRel = $referencePath

                # Normalize slashes
                $candidateRel = $candidateRel.Replace('\', '/')
                $originalRel = $originalRel.Replace('\', '/')

                # Check if the path depth matches
                $candidateDepth = ($candidateRel -split '/').Count
                $originalDepth = ($originalRel -split '/').Count

                if ($candidateDepth -eq $originalDepth) {
                    Write-VerboseMessage "Found match with same path depth: $candidate"
                    return $candidate
                }
            }

            # If no perfect match, return the first one
            Write-VerboseMessage "Using first available match: $($candidates[0])"
            return $candidates[0]
        }
    }

    return $null
}

# Process each project file
$totalProjects = 0
$fixedProjects = 0
$fixedReferences = 0

Get-ChildItem -Path $RootPath -Recurse -Filter "*.csproj" | ForEach-Object {
    $projectFile = $_
    $totalProjects++

    Write-Info "Processing: $($projectFile.Name)"

    $projectDir = $projectFile.DirectoryName
    $content = Get-Content -Path $projectFile.FullName -Raw
    $originalContent = $content

    # Find all ProjectReference elements
    $pattern = '<ProjectReference\s+Include="([^"]+)"'
    $matches = [regex]::Matches($content, $pattern)

    $hasChanges = $false

    foreach ($match in $matches) {
        $fullMatch = $match.Groups[0].Value
        $referencePath = $match.Groups[1].Value

        Write-VerboseMessage "  Checking reference: $referencePath"

        # Try to resolve the reference
        $normalizedRef = Normalize-Path $referencePath $projectDir

        if ($normalizedRef -and (Test-Path $normalizedRef)) {
            Write-VerboseMessage "  ✓ Reference exists: $referencePath"
            continue
        }

        # Reference doesn't exist - try to find the correct one
        $bestMatch = Find-BestMatch $referencePath $projectDir

        if ($bestMatch) {
            # Calculate the correct relative path
            $newRelativePath = [System.IO.Path]::GetRelativePath($projectDir, $bestMatch)
            $newRelativePath = $newRelativePath.Replace('/', '\')

            if ($referencePath -ne $newRelativePath) {
                Write-Change "  Fixing: $referencePath -> $newRelativePath"

                $newReference = $fullMatch -replace [regex]::Escape($referencePath), $newRelativePath
                $content = $content -replace [regex]::Escape($fullMatch), $newReference

                $hasChanges = $true
                $fixedReferences++
            }
        }
        else {
            Write-Warning "  Could not find match for: $referencePath"
        }
    }

    if ($hasChanges) {
        $fixedProjects++

        if (-not $DryRun) {
            # Backup original file
            $backupPath = "$($projectFile.FullName).bak"
            if (-not (Test-Path $backupPath)) {
                Copy-Item -Path $projectFile.FullName -Destination $backupPath
            }

            # Write updated content
            Set-Content -Path $projectFile.FullName -Value $content -NoNewline
            Write-Success "  Updated project file"
        }
        else {
            Write-Info "  Would update project file (dry run)"
        }
    }
}

Write-Success "`nProcessing complete!"
Write-Info "Total projects: $totalProjects"
Write-Info "Projects fixed: $fixedProjects"
Write-Info "References fixed: $fixedReferences"

if ($DryRun) {
    Write-Warning "`nThis was a dry run. No files were modified."
    Write-Info "Run without -DryRun to apply changes."
}

# Generate summary report
$reportPath = Join-Path $RootPath "reference-fix-report.txt"
@"
Reference Fix Report
===================
Date: $(Get-Date)
Total Projects: $totalProjects
Projects Fixed: $fixedProjects
References Fixed: $fixedReferences
Dry Run: $DryRun

Next Steps:
1. Build the solution to verify all references are correct
2. Check for any remaining build errors
3. If there are issues, check the .bak files for originals
"@ | Set-Content -Path $reportPath

Write-Info "`nReport saved to: $reportPath"

# Provide additional guidance
Write-Host "`nNext steps:" -ForegroundColor Magenta
Write-Host "1. Try building the solution: dotnet build"
Write-Host "2. If you see any remaining reference errors, run this script with -Verbose"
Write-Host "3. For stubborn issues, you may need to:"
Write-Host "   - Clear all bin/obj folders: Get-ChildItem -Recurse -Directory -Include bin,obj | Remove-Item -Recurse -Force"
Write-Host "   - Restore packages: dotnet restore --force"
