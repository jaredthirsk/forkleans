#!/usr/bin/env pwsh
param(
    [Parameter(Mandatory=$false)]
    [string]$PackageDirectory = "../../Artifacts/Release"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Convert to absolute path
$PackageDirectory = Resolve-Path $PackageDirectory

Write-Host "=== Fixing Granville.Rpc Package Dependencies ===" -ForegroundColor Cyan
Write-Host "Package directory: $PackageDirectory"

# Find all Granville.Rpc.* packages
$rpcPackages = @(Get-ChildItem -Path $PackageDirectory -Filter "Granville.Rpc.*.nupkg" | Where-Object { $_.Name -notmatch "symbols\.nupkg$" })

if ($rpcPackages.Count -eq 0) {
    Write-Host "No Granville.Rpc.* packages found in $PackageDirectory" -ForegroundColor Yellow
    exit 1
}

Write-Host "Found $($rpcPackages.Count) Granville.Rpc packages to fix" -ForegroundColor Green

foreach ($package in $rpcPackages) {
    Write-Host "`nProcessing: $($package.Name)" -ForegroundColor Yellow
    
    # Create temp directory for extraction
    $tempRoot = if ($env:TEMP) { $env:TEMP } else { "/tmp" }
    $tempDir = Join-Path $tempRoot "rpc-fix-$([System.Guid]::NewGuid().ToString('N'))"
    
    try {
        # Ensure temp directory is clean
        if (Test-Path $tempDir) {
            Remove-Item $tempDir -Recurse -Force
        }
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        
        # Extract package using Expand-Archive
        Expand-Archive -Path $package.FullName -DestinationPath $tempDir -Force
        
        # Find nuspec file
        $nuspecFile = Get-ChildItem -Path $tempDir -Filter "*.nuspec" | Select-Object -First 1
        if (-not $nuspecFile) {
            Write-Host "  WARNING: No nuspec file found in package" -ForegroundColor Red
            continue
        }
        
        # Read nuspec content
        $nuspecContent = Get-Content $nuspecFile.FullName -Raw
        $originalContent = $nuspecContent
        
        # Fix Microsoft.Orleans.* dependencies to use Granville.Orleans.*
        $pattern = '<dependency id="Microsoft\.Orleans\.([^"]+)" version="([^"]+)"'
        $nuspecContent = [regex]::Replace($nuspecContent, $pattern, {
            param($match)
            $orleansPackage = $match.Groups[1].Value
            $version = $match.Groups[2].Value
            
            Write-Host "  Fixing dependency: Microsoft.Orleans.$orleansPackage -> Granville.Orleans.$orleansPackage" -ForegroundColor Cyan
            return "<dependency id=`"Granville.Orleans.$orleansPackage`" version=`"$version`""
        })
        
        if ($nuspecContent -ne $originalContent) {
            # Write updated nuspec
            Set-Content -Path $nuspecFile.FullName -Value $nuspecContent -NoNewline
            
            # Delete original package
            Remove-Item $package.FullName -Force
            
            # Recreate package
            $packageName = $package.Name
            Compress-Archive -Path "$tempDir/*" -DestinationPath $package.FullName -CompressionLevel Optimal -Force
            
            Write-Host "  ✓ Package updated successfully" -ForegroundColor Green
        } else {
            Write-Host "  No Microsoft.Orleans.* dependencies found" -ForegroundColor Gray
        }
    }
    finally {
        # Clean up temp directory
        if (Test-Path $tempDir) {
            Remove-Item $tempDir -Recurse -Force
        }
    }
}

Write-Host "`n✓ RPC package dependency fixing complete!" -ForegroundColor Green