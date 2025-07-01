# Fix-PackageIds.ps1
# Updates PackageId from Microsoft.Orleans.* to Forkleans.*

param(
    [Parameter()]
    [string]$RootPath = ".",
    
    [Parameter()]
    [switch]$DryRun = $false
)

$ErrorActionPreference = "Stop"

Write-Host "Fixing PackageId values in .csproj files..." -ForegroundColor Cyan

# Find all csproj files
$projectFiles = Get-ChildItem -Path $RootPath -Recurse -Filter "*.csproj" | Where-Object {
    $_.FullName -notmatch "\\(bin|obj)\\"
}

$modifiedCount = 0

foreach ($file in $projectFiles) {
    $content = Get-Content $file.FullName -Raw
    $originalContent = $content
    
    # Replace Microsoft.Orleans.* PackageId with Forkleans.*
    $content = $content -replace '<PackageId>Microsoft\.Orleans\.', '<PackageId>Forkleans.'
    $content = $content -replace '<PackageId>Microsoft\.Forkleans\.', '<PackageId>Forkleans.'
    
    if ($content -ne $originalContent) {
        $modifiedCount++
        Write-Host "  Updating: $($file.Name)" -ForegroundColor Yellow
        
        if (-not $DryRun) {
            Set-Content -Path $file.FullName -Value $content -NoNewline
        }
    }
}

Write-Host "`nFixed $modifiedCount project files" -ForegroundColor Green

if ($DryRun) {
    Write-Host "This was a dry run. Run without -DryRun to apply changes." -ForegroundColor Cyan
}