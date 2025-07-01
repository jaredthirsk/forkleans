# Fix-BuildPropFileNames.ps1
# Renames Microsoft.Orleans.* props/targets files to Forkleans.* in build directories

param(
    [Parameter()]
    [string]$RootPath = ".",
    
    [Parameter()]
    [switch]$DryRun = $false
)

$ErrorActionPreference = "Stop"

Write-Host "Fixing build props/targets file names..." -ForegroundColor Cyan

# Find all Microsoft.Orleans.*.props and *.targets files in build directories
$filesToRename = Get-ChildItem -Path $RootPath -Recurse -Include "Microsoft.Orleans.*.props", "Microsoft.Orleans.*.targets" | 
    Where-Object { 
        $_.DirectoryName -match "[/\\]build(MultiTargeting|Transitive)?$" -and
        $_.FullName -notmatch "[/\\](bin|obj)[/\\]"
    }

$renamedCount = 0

foreach ($file in $filesToRename) {
    # Generate new name: Microsoft.Orleans.X -> Forkleans.X
    $newName = $file.Name -replace "^Microsoft\.Orleans\.", "Forkleans."
    $newPath = Join-Path $file.DirectoryName $newName
    
    Write-Host "  Renaming: $($file.Name) -> $newName" -ForegroundColor Yellow
    
    if (-not $DryRun) {
        Move-Item -Path $file.FullName -Destination $newPath -Force
        $renamedCount++
    }
}

# Now update project files that reference these files
Write-Host "`nUpdating project file references..." -ForegroundColor Cyan

$projectFiles = Get-ChildItem -Path $RootPath -Recurse -Filter "*.csproj" | 
    Where-Object { $_.FullName -notmatch "[/\\](bin|obj)[/\\]" }

$updatedProjects = 0

foreach ($project in $projectFiles) {
    $content = Get-Content $project.FullName -Raw
    $originalContent = $content
    
    # Update references to the renamed files
    $content = $content -replace 'Microsoft\.Orleans\.([\w\.]+)\.(props|targets)"', 'Forkleans.$1.$2"'
    
    if ($content -ne $originalContent) {
        Write-Host "  Updating: $($project.Name)" -ForegroundColor Yellow
        
        if (-not $DryRun) {
            Set-Content -Path $project.FullName -Value $content -NoNewline
            $updatedProjects++
        }
    }
}

Write-Host "`nSummary:" -ForegroundColor Green
Write-Host "  Files renamed: $renamedCount" -ForegroundColor Gray
Write-Host "  Projects updated: $updatedProjects" -ForegroundColor Gray

if ($DryRun) {
    Write-Host "`nThis was a dry run. Run without -DryRun to apply changes." -ForegroundColor Cyan
}