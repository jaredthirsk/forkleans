# Simple script to fix project references from Forkleans.* back to Orleans.*
# This corrects the issue where the namespace converter incorrectly changed file paths

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

Write-Info "Fixing project references in: $RootPath"
Write-Info "Dry run: $DryRun"

$processedFiles = 0
$modifiedFiles = 0
$totalReferencesFixed = 0

# Find all project files
Get-ChildItem -Path $RootPath -Recurse -Include "*.csproj", "*.fsproj" | ForEach-Object {
    $file = $_
    $processedFiles++
    
    try {
        $content = Get-Content -Path $file.FullName -Raw
        $originalContent = $content
        
        # Fix Orleans project references that were incorrectly changed to Forkleans
        # This regex will match project references to Forkleans.*.csproj and change them back to Orleans.*.csproj
        $pattern = '(<ProjectReference\s+Include="[^"]*[\\/])Forkleans\.([^"]*\.(cs|fs)proj")'
        $newContent = $content -replace $pattern, '${1}Orleans.${2}'
        
        if ($newContent -ne $originalContent) {
            $modifiedFiles++
            $relativePath = $file.FullName.Substring($RootPath.Length + 1)
            
            # Count how many references were fixed
            $matches = [regex]::Matches($originalContent, $pattern)
            $referenceCount = $matches.Count
            $totalReferencesFixed += $referenceCount
            
            Write-Change "$relativePath"
            Write-Host "  Fixed $referenceCount project reference(s)" -ForegroundColor Gray
            
            if (-not $DryRun) {
                Set-Content -Path $file.FullName -Value $newContent -NoNewline -Force
            }
        }
    }
    catch {
        Write-Error "Failed to process $($file.FullName): $_"
    }
}

Write-Success "Processing complete!"
Write-Info "Files processed: $processedFiles"
Write-Info "Files modified: $modifiedFiles"
Write-Info "Total references fixed: $totalReferencesFixed"

if ($modifiedFiles -gt 0 -and -not $DryRun) {
    Write-Info ""
    Write-Info "Project references have been fixed."
    Write-Info "You should now run 'dotnet restore' to update package dependencies."
}