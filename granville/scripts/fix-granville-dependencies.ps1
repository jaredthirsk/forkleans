#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory=$false)]
    [string]$PackageDirectory = "../../Artifacts/Release"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Convert to absolute path
$PackageDirectory = Resolve-Path $PackageDirectory

Write-Host "=== Fixing Granville Package Dependencies ===" -ForegroundColor Cyan
Write-Host "Package directory: $PackageDirectory"

# Find all Granville.Orleans.* packages
$granvillePackages = Get-ChildItem -Path $PackageDirectory -Filter "Granville.Orleans.*.nupkg" | Where-Object { $_.Name -notmatch "symbols\.nupkg$" }

if ($granvillePackages.Count -eq 0) {
    Write-Host "No Granville.Orleans.* packages found in $PackageDirectory" -ForegroundColor Yellow
    exit 1
}

Write-Host "Found $($granvillePackages.Count) Granville packages to fix" -ForegroundColor Green

foreach ($package in $granvillePackages) {
    Write-Host "`nProcessing: $($package.Name)" -ForegroundColor Yellow
    
    # Create temp directory for extraction
    $tempRoot = if ($env:TEMP) { $env:TEMP } else { Join-Path $PackageDirectory "temp" }
    $tempDir = Join-Path $tempRoot "granville-fix-$([System.Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
    
    try {
        # Extract package
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::ExtractToDirectory($package.FullName, $tempDir)
        
        # Find nuspec file
        $nuspecFile = Get-ChildItem -Path $tempDir -Filter "*.nuspec" | Select-Object -First 1
        if (-not $nuspecFile) {
            Write-Host "  WARNING: No nuspec file found in package" -ForegroundColor Red
            continue
        }
        
        # Read nuspec content
        $nuspecContent = Get-Content $nuspecFile.FullName -Raw
        $originalContent = $nuspecContent
        
        # Fix Microsoft.Orleans.* dependencies to use -granville-shim suffix
        $pattern = '<dependency id="(Microsoft\.Orleans\.[^"]+)" version="([^"]+)"'
        $nuspecContent = [regex]::Replace($nuspecContent, $pattern, {
            param($match)
            $id = $match.Groups[1].Value
            $version = $match.Groups[2].Value
            
            # Only add suffix if it doesn't already have it
            if ($version -notmatch '-granville-shim$') {
                Write-Host "  Fixing dependency: $id $version -> $version-granville-shim" -ForegroundColor Cyan
                return "<dependency id=`"$id`" version=`"$version-granville-shim`""
            }
            return $match.Value
        })
        
        if ($nuspecContent -ne $originalContent) {
            # Write updated nuspec
            Set-Content -Path $nuspecFile.FullName -Value $nuspecContent -NoNewline
            
            # Delete original package
            Remove-Item $package.FullName -Force
            
            # Recreate package
            $packageName = $package.Name
            [System.IO.Compression.ZipFile]::CreateFromDirectory($tempDir, $package.FullName, [System.IO.Compression.CompressionLevel]::Optimal, $false)
            
            Write-Host "  ✓ Package updated successfully" -ForegroundColor Green
        } else {
            Write-Host "  No Microsoft.Orleans.* dependencies found or already fixed" -ForegroundColor Gray
        }
    }
    finally {
        # Clean up temp directory
        if (Test-Path $tempDir) {
            Remove-Item $tempDir -Recurse -Force
        }
    }
}

Write-Host "`n✓ Dependency fixing complete!" -ForegroundColor Green