#!/usr/bin/env pwsh
# Fix the Granville.Orleans.Sdk package to have correct target file names

param(
    [string]$PackagePath,
    [string]$Version
)

# Read version from Directory.Build.props if not provided
if (!$Version) {
    $directoryBuildProps = Join-Path $PSScriptRoot "../../Directory.Build.props"
    if (Test-Path $directoryBuildProps) {
        $xml = [xml](Get-Content $directoryBuildProps)
        $versionPrefix = $xml.SelectSingleNode("//VersionPrefix").InnerText
        $granvilleRevision = $xml.SelectSingleNode("//GranvilleRevision").InnerText
        $Version = "$versionPrefix.$granvilleRevision"
    } else {
        Write-Error "Version parameter is required or Directory.Build.props must exist with VersionPrefix and GranvilleRevision"
        exit 1
    }
}

# Set default package path if not provided
if (!$PackagePath) {
    $PackagePath = "Artifacts/Release/Granville.Orleans.Sdk.$Version.nupkg"
}

Write-Host "Fixing Granville.Orleans.Sdk package..." -ForegroundColor Green

# Create temp directory
$tempDir = New-Item -ItemType Directory -Force -Path "temp-sdk-fix"

# Extract package
Write-Host "Extracting package..." -ForegroundColor Cyan
Push-Location $tempDir
try {
    Expand-Archive -Path "../$PackagePath" -DestinationPath "." -Force
    
    # Rename target files
    Write-Host "Renaming target files..." -ForegroundColor Cyan
    if (Test-Path "build/Microsoft.Orleans.Sdk.targets") {
        Move-Item "build/Microsoft.Orleans.Sdk.targets" "build/Granville.Orleans.Sdk.targets" -Force
    }
    if (Test-Path "buildTransitive/Microsoft.Orleans.Sdk.targets") {
        Move-Item "buildTransitive/Microsoft.Orleans.Sdk.targets" "buildTransitive/Granville.Orleans.Sdk.targets" -Force
    }
    if (Test-Path "buildMultiTargeting/Microsoft.Orleans.Sdk.targets") {
        Move-Item "buildMultiTargeting/Microsoft.Orleans.Sdk.targets" "buildMultiTargeting/Granville.Orleans.Sdk.targets" -Force
    }
    
    # Update nuspec file
    Write-Host "Updating nuspec..." -ForegroundColor Cyan
    $nuspecFile = Get-ChildItem -Filter "*.nuspec" | Select-Object -First 1
    if ($nuspecFile) {
        $content = Get-Content $nuspecFile.FullName -Raw
        $content = $content -replace 'Microsoft\.Orleans\.Sdk\.targets', 'Granville.Orleans.Sdk.targets'
        $content | Set-Content $nuspecFile.FullName -NoNewline
    }
    
    # Update contents of targets files
    Write-Host "Updating targets file contents..." -ForegroundColor Cyan
    Get-ChildItem -Recurse -Filter "*.targets" | ForEach-Object {
        $content = Get-Content $_.FullName -Raw
        $content = $content -replace 'Microsoft\.Orleans\.Sdk', 'Granville.Orleans.Sdk'
        $content = $content -replace 'Microsoft\.Orleans\.CodeGenerator', 'Granville.Orleans.CodeGenerator'
        $content = $content -replace 'Microsoft\.Orleans\.Analyzers', 'Granville.Orleans.Analyzers'
        $content | Set-Content $_.FullName -NoNewline
    }
    
    # Recreate package
    Write-Host "Creating fixed package..." -ForegroundColor Cyan
    Remove-Item "../$PackagePath" -Force
    Compress-Archive -Path * -DestinationPath "../$PackagePath" -Force
    
    # Rename .zip to .nupkg
    Move-Item "../$PackagePath.zip" "../$PackagePath" -Force -ErrorAction SilentlyContinue
    
    Write-Host "Package fixed successfully!" -ForegroundColor Green
}
finally {
    Pop-Location
    Remove-Item -Recurse -Force $tempDir
}