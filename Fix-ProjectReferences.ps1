# Fix-ProjectReferences.ps1
# This script fixes project references after namespace conversion
# It handles the mismatch between physical folder names and project references

param(
    [Parameter(Mandatory=$true)]
    [string]$RootPath,
    
    [Parameter()]
    [string]$OldName = "Orleans",
    
    [Parameter()]
    [string]$NewName = "Forkleans",
    
    [Parameter()]
    [switch]$DryRun = $false,
    
    [Parameter()]
    [ValidateSet("KeepPaths", "UpdatePaths", "CreateMapping")]
    [string]$Strategy = "KeepPaths"
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

# Create a mapping of old to new project files
$projectMapping = @{}
$allProjects = Get-ChildItem -Path $RootPath -Recurse -Filter "*.csproj"

Write-Info "Building project mapping..."

foreach ($project in $allProjects) {
    # Get the project name from the file
    $projectName = $project.BaseName
    
    # Determine what the new name should be
    $newProjectName = $projectName -replace "^$OldName", $NewName
    
    # Store both possible references
    $relativePath = $project.FullName.Substring($RootPath.Length + 1)
    $projectMapping[$projectName] = @{
        OldPath = $relativePath
        NewName = $newProjectName
        FullPath = $project.FullName
        Directory = $project.DirectoryName
    }
}

Write-Info "Found $($projectMapping.Count) projects"

# Function to fix project references based on strategy
function Fix-ProjectReference($line, $csprojPath) {
    if ($line -match '<ProjectReference\s+Include="([^"]+)"') {
        $referencePath = $matches[1]
        $resolvedPath = Join-Path (Split-Path $csprojPath -Parent) $referencePath
        
        # Try to resolve the actual file
        $normalizedPath = [System.IO.Path]::GetFullPath($resolvedPath)
        
        # Check if file exists with original name
        if (Test-Path $normalizedPath) {
            # File exists, keep the path as is
            Write-Info "  Reference exists: $referencePath"
            return $line
        }
        
        # Try with modified filename but same path
        $directory = Split-Path $referencePath -Parent
        $filename = Split-Path $referencePath -Leaf
        $newFilename = $filename -replace "^$OldName", $NewName
        
        if ($filename -ne $newFilename) {
            $newReferencePath = if ($directory) { "$directory\$newFilename" } else { $newFilename }
            $newResolvedPath = Join-Path (Split-Path $csprojPath -Parent) $newReferencePath
            
            if (Test-Path $newResolvedPath) {
                Write-Change "  Updating reference: $referencePath -> $newReferencePath"
                return $line -replace [regex]::Escape($referencePath), $newReferencePath
            }
        }
        
        # If strategy is CreateMapping, create a mapping file
        if ($Strategy -eq "CreateMapping") {
            Add-Content -Path "$RootPath\project-reference-mapping.txt" -Value "$csprojPath : $referencePath -> NOT FOUND"
        }
        
        Write-Warning "  Could not resolve reference: $referencePath"
    }
    
    return $line
}

# Process all csproj files
$processedFiles = 0
$modifiedFiles = 0

foreach ($project in $allProjects) {
    $processedFiles++
    $modified = $false
    
    Write-Info "Processing: $($project.Name)"
    
    try {
        $lines = Get-Content -Path $project.FullName
        $newLines = @()
        
        foreach ($line in $lines) {
            $newLine = Fix-ProjectReference $line $project.FullName
            if ($newLine -ne $line) {
                $modified = $true
            }
            $newLines += $newLine
        }
        
        if ($modified) {
            $modifiedFiles++
            if (-not $DryRun) {
                $newLines | Set-Content -Path $project.FullName -Force
                Write-Success "  Updated project file"
            } else {
                Write-Info "  Would update project file (dry run)"
            }
        }
    }
    catch {
        Write-Error "Failed to process $($project.Name): $_"
    }
}

Write-Success "Processing complete!"
Write-Info "Files processed: $processedFiles"
Write-Info "Files modified: $modifiedFiles"

# Provide additional guidance
Write-Host "`nRecommendations based on your setup:" -ForegroundColor Magenta

if ($Strategy -eq "KeepPaths") {
    Write-Host @"
Since you're keeping the original folder structure:
1. The script has updated project references to match actual file locations
2. You may need to manually update some solution file references
3. Consider creating a Directory.Build.props file to centralize namespace configuration

Example Directory.Build.props:
<Project>
  <PropertyGroup>
    <RootNamespace>$NewName</RootNamespace>
    <AssemblyName>$NewName.$([System.IO.Path]::GetFileNameWithoutExtension('$(MSBuildProjectName)'))</AssemblyName>
  </PropertyGroup>
</Project>
"@
}

# Generate a summary of issues found
$unmappedReferences = Get-Content "$RootPath\project-reference-mapping.txt" -ErrorAction SilentlyContinue
if ($unmappedReferences) {
    Write-Warning "`nFound unresolved references:"
    $unmappedReferences | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
}
