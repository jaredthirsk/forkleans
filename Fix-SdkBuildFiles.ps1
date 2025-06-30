# Fix SDK build file names from Microsoft.Forkleans.* to Microsoft.Orleans.*
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

Write-Info "Fixing SDK build file names in: $RootPath"
Write-Info "Dry run: $DryRun"

$renamedFiles = 0

# Find all Microsoft.Forkleans.* files in build directories
Get-ChildItem -Path $RootPath -Recurse -Filter "Microsoft.Forkleans.*" | Where-Object { 
    $_.DirectoryName -match "(build|buildMultiTargeting|buildTransitive)"
} | ForEach-Object {
    $file = $_
    $oldName = $file.Name
    $newName = $oldName -replace "Microsoft\.Forkleans\.", "Microsoft.Orleans."
    $newPath = Join-Path $file.DirectoryName $newName
    
    $relativePath = $file.FullName.Substring($RootPath.Length + 1)
    Write-Change "Renaming: $relativePath -> $newName"
    
    if (-not $DryRun) {
        Move-Item -Path $file.FullName -Destination $newPath -Force
        $renamedFiles++
    }
}

# Also check Orleans.CodeGenerator build files
$codeGenPath = Join-Path $RootPath "src/Orleans.CodeGenerator/build"
if (Test-Path $codeGenPath) {
    Get-ChildItem -Path $codeGenPath -Filter "Microsoft.Forkleans.*" | ForEach-Object {
        $file = $_
        $oldName = $file.Name
        $newName = $oldName -replace "Microsoft\.Forkleans\.", "Microsoft.Orleans."
        $newPath = Join-Path $file.DirectoryName $newName
        
        Write-Change "Renaming: $($file.Name) -> $newName"
        
        if (-not $DryRun) {
            Move-Item -Path $file.FullName -Destination $newPath -Force
            $renamedFiles++
        }
    }
}

Write-Success "Renamed $renamedFiles file(s)"

if ($renamedFiles -gt 0 -and -not $DryRun) {
    Write-Info ""
    Write-Info "SDK build files have been renamed."
}