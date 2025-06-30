# Comprehensive script to fix all project reference issues
# This handles both the Forkleans prefix in paths and ensures correct references

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

Write-Info "Fixing ALL project references in: $RootPath"
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
        $changes = 0
        
        # Fix pattern 1: Forkleans in the path (e.g., src\Forkleans.Core\Orleans.Core.csproj)
        # This pattern catches paths like "..\..\src\Forkleans.Something\Orleans.Something.csproj"
        $pattern1 = '(<ProjectReference\s+Include="[^"]*[\\/])Forkleans\.([^"\\\/]*)([\\/]Orleans\.[^"]*\.(?:cs|fs)proj")'
        $newContent = $content -replace $pattern1, '${1}Orleans.${2}${3}'
        if ($newContent -ne $content) {
            $changes += ([regex]::Matches($content, $pattern1)).Count
            $content = $newContent
        }
        
        # Fix pattern 2: Direct Forkleans project references (e.g., Forkleans.Core.csproj)
        # This catches direct references that were incorrectly renamed
        $pattern2 = '(<ProjectReference\s+Include="[^"]*[\\/])Forkleans\.([^"]*\.(?:cs|fs)proj")'
        $newContent = $content -replace $pattern2, '${1}Orleans.${2}'
        if ($newContent -ne $content) {
            $changes += ([regex]::Matches($content, $pattern2)).Count
            $content = $newContent
        }
        
        # Fix pattern 3: Fix TestInternalDtosRefForkleans back to TestInternalDtosRefOrleans
        $pattern3 = 'TestInternalDtosRefForkleans'
        $newContent = $content -replace $pattern3, 'TestInternalDtosRefOrleans'
        if ($newContent -ne $content) {
            $changes += ([regex]::Matches($content, $pattern3)).Count
            $content = $newContent
        }
        
        # Fix pattern 4: Fix mismatched OrleansBuildTimeCodeGen tags
        # Handle case where opening tag is Orleans but closing is Forkleans
        $pattern4a = '<OrleansBuildTimeCodeGen>([^<]*)</ForkleansBuildTimeCodeGen>'
        $newContent = $content -replace $pattern4a, '<OrleansBuildTimeCodeGen>$1</OrleansBuildTimeCodeGen>'
        if ($newContent -ne $content) {
            $changes += ([regex]::Matches($content, $pattern4a)).Count
            $content = $newContent
        }
        
        # Also fix any remaining ForkleansBuildTimeCodeGen tags
        $pattern4b = 'ForkleansBuildTimeCodeGen'
        $newContent = $content -replace $pattern4b, 'OrleansBuildTimeCodeGen'
        if ($newContent -ne $content) {
            $changes += ([regex]::Matches($content, $pattern4b)).Count
            $content = $newContent
        }
        
        if ($content -ne $originalContent) {
            $modifiedFiles++
            $relativePath = $file.FullName.Substring($RootPath.Length + 1)
            
            $totalReferencesFixed += $changes
            
            Write-Change "$relativePath"
            Write-Host "  Fixed $changes issue(s)" -ForegroundColor Gray
            
            if (-not $DryRun) {
                Set-Content -Path $file.FullName -Value $content -NoNewline -Force
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
Write-Info "Total issues fixed: $totalReferencesFixed"

if ($modifiedFiles -gt 0 -and -not $DryRun) {
    Write-Info ""
    Write-Info "Project references have been fixed."
    Write-Info "You should now run 'dotnet restore' to update package dependencies."
}