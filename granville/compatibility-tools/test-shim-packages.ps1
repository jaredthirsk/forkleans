#!/usr/bin/env pwsh

# Test script to verify shim packages exist and have correct structure

$ErrorActionPreference = "Stop"

# Read version from Directory.Build.props
$directoryBuildProps = Join-Path $PSScriptRoot "../../Directory.Build.props"
if (Test-Path $directoryBuildProps) {
    $xml = [xml](Get-Content $directoryBuildProps)
    $versionPrefix = $xml.SelectSingleNode("//VersionPrefix").InnerText
    $granvilleRevision = $xml.SelectSingleNode("//GranvilleRevision").InnerText
    $version = "$versionPrefix.$granvilleRevision-granville-shim"
    Write-Host "Checking for shim packages version: $version" -ForegroundColor Yellow
} else {
    Write-Error "Directory.Build.props not found"
    exit 1
}

$outputDir = "$PSScriptRoot/../../Artifacts/Release"

# List of expected shim packages
$expectedPackages = @(
    "Microsoft.Orleans.Core.Abstractions",
    "Microsoft.Orleans.Core",
    "Microsoft.Orleans.Runtime",
    "Microsoft.Orleans.Serialization.Abstractions",
    "Microsoft.Orleans.Serialization",
    "Microsoft.Orleans.CodeGenerator",
    "Microsoft.Orleans.Analyzers",
    "Microsoft.Orleans.Sdk"
)

$missingPackages = @()
$foundPackages = @()

foreach ($package in $expectedPackages) {
    $packagePath = "$outputDir/$package.$version.nupkg"
    if (Test-Path $packagePath) {
        $foundPackages += $package
        Write-Host "  ✓ Found $package" -ForegroundColor Green
        
        # Check if it's a props package
        if ($package -in @("Microsoft.Orleans.CodeGenerator", "Microsoft.Orleans.Sdk")) {
            # Check for props file in package
            $tempExtract = "$PSScriptRoot/temp-extract-$package"
            Remove-Item -Recurse -Force $tempExtract -ErrorAction SilentlyContinue
            New-Item -ItemType Directory -Force -Path $tempExtract | Out-Null
            
            try {
                Add-Type -AssemblyName System.IO.Compression.FileSystem
                [System.IO.Compression.ZipFile]::ExtractToDirectory($packagePath, $tempExtract)
                
                $propsFile = Get-ChildItem -Path $tempExtract -Filter "*.props" -Recurse
                if ($propsFile) {
                    Write-Host "    → Contains props file: $($propsFile.Name)" -ForegroundColor Gray
                    
                    # Check for Orleans_DesignTimeBuild property
                    $propsContent = Get-Content $propsFile.FullName -Raw
                    if ($propsContent -match "Orleans_DesignTimeBuild") {
                        Write-Host "    → Contains Orleans_DesignTimeBuild property" -ForegroundColor Cyan
                    }
                }
            } finally {
                Remove-Item -Recurse -Force $tempExtract -ErrorAction SilentlyContinue
            }
        }
    } else {
        $missingPackages += $package
        Write-Host "  ✗ Missing $package" -ForegroundColor Red
    }
}

Write-Host "`nSummary:" -ForegroundColor Yellow
Write-Host "  Found: $($foundPackages.Count) packages" -ForegroundColor Green
Write-Host "  Missing: $($missingPackages.Count) packages" -ForegroundColor Red

if ($missingPackages.Count -gt 0) {
    Write-Host "`nTo create missing packages, run:" -ForegroundColor Yellow
    Write-Host "  ./compile-all-shims.ps1" -ForegroundColor Gray
    Write-Host "  ./package-minimal-shims.ps1" -ForegroundColor Gray
    exit 1
} else {
    Write-Host "`nAll shim packages are present!" -ForegroundColor Green
}