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

if (-not $granvillePackages -or @($granvillePackages).Count -eq 0) {
    Write-Host "No Granville.Orleans.* packages found in $PackageDirectory" -ForegroundColor Yellow
    exit 1
}

Write-Host "Found $($granvillePackages.Count) Granville packages to fix" -ForegroundColor Green

foreach ($package in $granvillePackages) {
    Write-Host "`nProcessing: $($package.Name)" -ForegroundColor Yellow
    
    # Create temp directory for extraction
    $tempRoot = if ($env:TEMP) { $env:TEMP } else { "/tmp" }
    $tempDir = Join-Path $tempRoot "granville-fix-$([System.Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
    
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
        
        # Define the specific Microsoft.Orleans packages we have shims for
        $shimmedPackages = @(
            "Microsoft.Orleans.Core",
            "Microsoft.Orleans.Core.Abstractions",
            "Microsoft.Orleans.Runtime",
            "Microsoft.Orleans.Serialization",
            "Microsoft.Orleans.Serialization.Abstractions"
        )
        
        # Fix only shimmed Microsoft.Orleans.* dependencies to use -granville-shim suffix
        $pattern = '<dependency id="(Microsoft\.Orleans\.[^"]+)" version="([^"]+)"'
        $nuspecContent = [regex]::Replace($nuspecContent, $pattern, {
            param($match)
            $id = $match.Groups[1].Value
            $version = $match.Groups[2].Value
            
            # Check if this is an analyzer/codegenerator dependency
            $isAnalyzer = ($id -eq "Microsoft.Orleans.Analyzers" -or $id -eq "Microsoft.Orleans.CodeGenerator")
            
            if ($isAnalyzer) {
                # Replace Microsoft.Orleans.Analyzers/CodeGenerator with Granville.Orleans.Analyzers/CodeGenerator
                $newId = $id -replace "Microsoft\.Orleans\.", "Granville.Orleans."
                Write-Host "  Fixing analyzer dependency: $id -> $newId" -ForegroundColor Cyan
                # Replace the entire id, preserving the rest of the attributes
                return $match.Value -replace "id=`"$id`"", "id=`"$newId`""
            }
            elseif ($shimmedPackages -contains $id) {
                # Only add suffix if it doesn't already have it
                if ($version -notmatch '-granville-shim$') {
                    Write-Host "  Fixing dependency: $id $version -> $version-granville-shim" -ForegroundColor Cyan
                    return "<dependency id=`"$id`" version=`"$version-granville-shim`""
                }
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
            Compress-Archive -Path "$tempDir/*" -DestinationPath $package.FullName -CompressionLevel Optimal -Force
            
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