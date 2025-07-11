#!/usr/bin/env pwsh

# Script to create Granville.Orleans.CodeGenerator and Granville.Orleans.Analyzers packages
# by copying from Microsoft.Orleans versions

$ErrorActionPreference = "Stop"

# Read version from Directory.Build.props
$directoryBuildProps = Join-Path $PSScriptRoot "../../Directory.Build.props"
if (Test-Path $directoryBuildProps) {
    $xml = [xml](Get-Content $directoryBuildProps)
    $versionPrefix = $xml.SelectSingleNode("//VersionPrefix").InnerText
    $granvilleRevision = $xml.SelectSingleNode("//GranvilleRevision").InnerText
    $version = "$versionPrefix.$granvilleRevision"
    Write-Host "Using version from Directory.Build.props: $version" -ForegroundColor Yellow
} else {
    Write-Error "Directory.Build.props must exist with VersionPrefix and GranvilleRevision"
    exit 1
}
$outputDir = "$PSScriptRoot/../../Artifacts/Release"
$tempDir = "$PSScriptRoot/temp-repackage"

# Packages to repackage
$packages = @(
    @{
        Source = "Microsoft.Orleans.CodeGenerator"
        Target = "Granville.Orleans.CodeGenerator"
    },
    @{
        Source = "Microsoft.Orleans.Analyzers"
        Target = "Granville.Orleans.Analyzers"
    }
)

foreach ($package in $packages) {
    Write-Host "Repackaging $($package.Source) to $($package.Target)..." -ForegroundColor Cyan
    
    $sourcePackage = "$outputDir/$($package.Source).$version.nupkg"
    if (!(Test-Path $sourcePackage)) {
        Write-Error "Source package not found: $sourcePackage"
        continue
    }
    
    # Create temp directory for extraction
    $extractDir = "$tempDir/$($package.Target)"
    Remove-Item -Recurse -Force $extractDir -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $extractDir | Out-Null
    
    # Extract the package
    Write-Host "  Extracting $($package.Source)..." -ForegroundColor Gray
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($sourcePackage, $extractDir)
    
    # Update the nuspec file
    $nuspecFiles = Get-ChildItem -Path $extractDir -Filter "*.nuspec"
    foreach ($nuspecFile in $nuspecFiles) {
        Write-Host "  Updating nuspec: $($nuspecFile.Name)" -ForegroundColor Gray
        
        $content = Get-Content $nuspecFile.FullName -Raw
        
        # Replace package ID
        $content = $content -replace "<id>$($package.Source)</id>", "<id>$($package.Target)</id>"
        
        # Update dependencies from Microsoft.Orleans to Granville.Orleans
        $content = $content -replace 'id="Microsoft\.Orleans\.', 'id="Granville.Orleans.'
        
        # Update description
        $content = $content -replace "<description>(.+?)</description>", "<description>Granville fork of `$1</description>"
        
        # Save the updated nuspec
        $content | Out-File -FilePath $nuspecFile.FullName -Encoding UTF8
    }
    
    # Pack the package
    Write-Host "  Packing $($package.Target)..." -ForegroundColor Gray
    $nuspecPath = Get-ChildItem -Path $extractDir -Filter "*.nuspec" | Select-Object -First 1
    
    # Convert paths to Windows format for nuget.exe
    $winNuspecPath = $nuspecPath.FullName -replace '^/mnt/([a-z])/', '$1:/' -replace '/', '\'
    $winOutputDir = $outputDir -replace '^/mnt/([a-z])/', '$1:/' -replace '/', '\'
    
    & "$PSScriptRoot/nuget.exe" pack $winNuspecPath -OutputDirectory $winOutputDir -NoDefaultExcludes
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to pack $($package.Target)"
    } else {
        Write-Host "  ✓ Created $($package.Target).$version.nupkg" -ForegroundColor Green
    }
}

# Clean up temp directory
Remove-Item -Recurse -Force $tempDir -ErrorAction SilentlyContinue

Write-Host "`nGranville CodeGenerator and Analyzers packages created successfully!" -ForegroundColor Green