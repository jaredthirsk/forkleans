# Fix the mismatch between ForkleansBuildTimeCodeGen and OrleansBuildTimeCodeGen properties
# This script changes ForkleansBuildTimeCodeGen to OrleansBuildTimeCodeGen in project files

param(
    [string]$Path = ".",
    [switch]$DryRun = $false
)

$ErrorActionPreference = "Stop"

Write-Host "Scanning for project files with ForkleansBuildTimeCodeGen property..." -ForegroundColor Cyan

# Get all project files
$projectFiles = Get-ChildItem -Path $Path -Recurse -Include "*.csproj", "*.fsproj" | Where-Object {
    $_.FullName -notmatch '\\(bin|obj|\.git|artifacts|packages)\\'
}

$fixedFiles = 0
$checkedFiles = 0

foreach ($file in $projectFiles) {
    $checkedFiles++
    
    try {
        $content = Get-Content $file.FullName -Raw
        if ([string]::IsNullOrWhiteSpace($content)) {
            continue
        }
        
        $originalContent = $content
        
        # Replace ForkleansBuildTimeCodeGen with OrleansBuildTimeCodeGen
        if ($content -match '<ForkleansBuildTimeCodeGen>') {
            $content = $content -replace '<ForkleansBuildTimeCodeGen>', '<OrleansBuildTimeCodeGen>'
            $content = $content -replace '</ForkleansBuildTimeCodeGen>', '</OrleansBuildTimeCodeGen>'
            
            $fixedFiles++
            Write-Host "Fixed: $($file.FullName)" -ForegroundColor Yellow
            
            if (-not $DryRun) {
                Set-Content -Path $file.FullName -Value $content -NoNewline
            }
        }
    }
    catch {
        Write-Error "Error processing $($file.FullName): $_"
    }
}

Write-Host "`nSummary:" -ForegroundColor Green
Write-Host "Project files checked: $checkedFiles"
Write-Host "Project files fixed: $fixedFiles"

if ($DryRun) {
    Write-Host "`nThis was a dry run. No files were actually modified." -ForegroundColor Cyan
}

# Also check if Directory.Build.props needs updating
$directoryBuildProps = Join-Path $Path "Directory.Build.props"
if (Test-Path $directoryBuildProps) {
    Write-Host "`nChecking Directory.Build.props..." -ForegroundColor Cyan
    $content = Get-Content $directoryBuildProps -Raw
    
    if ($content -match "OrleansBuildTimeCodeGen" -and $content -notmatch "ForkleansBuildTimeCodeGen") {
        Write-Host "Directory.Build.props already uses OrleansBuildTimeCodeGen - good!" -ForegroundColor Green
    } elseif ($content -match "ForkleansBuildTimeCodeGen") {
        Write-Host "Directory.Build.props needs to be updated to use OrleansBuildTimeCodeGen instead of ForkleansBuildTimeCodeGen" -ForegroundColor Yellow
        
        if (-not $DryRun) {
            $content = $content -replace 'ForkleansBuildTimeCodeGen', 'OrleansBuildTimeCodeGen'
            Set-Content -Path $directoryBuildProps -Value $content -NoNewline
            Write-Host "Updated Directory.Build.props" -ForegroundColor Green
        }
    }
}