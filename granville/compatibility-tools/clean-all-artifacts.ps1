#!/usr/bin/env pwsh

$ErrorActionPreference = "Stop"

Write-Host "=== Cleaning all artifacts and build outputs ===" -ForegroundColor Cyan

# Clean NuGet packages from local cache
Write-Host "`nCleaning NuGet packages from local cache..." -ForegroundColor Yellow
$packagesToClean = @(
    "Microsoft.Orleans.Core",
    "Microsoft.Orleans.Core.Abstractions",
    "Microsoft.Orleans.Runtime",
    "Microsoft.Orleans.Serialization",
    "Microsoft.Orleans.Serialization.Abstractions",
    "Microsoft.Orleans.CodeGenerator",
    "Microsoft.Orleans.Sdk"
)

foreach ($package in $packagesToClean) {
    Write-Host "  Cleaning $package..." -ForegroundColor Gray
    & dotnet-win nuget locals all -c 2>&1 | Out-Null
    $cachePath = "$env:USERPROFILE\.nuget\packages\$($package.ToLower())"
    if (Test-Path $cachePath) {
        Remove-Item -Path $cachePath -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "    ✓ Cleaned $package" -ForegroundColor Green
    }
}

# Clean all bin and obj folders in the samples
Write-Host "`nCleaning bin and obj folders in samples..." -ForegroundColor Yellow
$samplesToClean = @(
    "../samples/Rpc/Shooter.Silo",
    "../samples/Rpc/Shooter.ActionServer",
    "../samples/Rpc/Shooter.Client",
    "../samples/Rpc/Shooter.Shared",
    "../samples/Rpc/Shooter.ServiceDefaults",
    "../samples/Rpc/Shooter.AppHost",
    "../samples/Rpc/Shooter.Bot"
)

foreach ($sample in $samplesToClean) {
    if (Test-Path $sample) {
        Write-Host "  Cleaning $sample..." -ForegroundColor Gray
        
        # Remove bin folder
        $binPath = Join-Path $sample "bin"
        if (Test-Path $binPath) {
            Remove-Item -Path $binPath -Recurse -Force
            Write-Host "    ✓ Removed bin" -ForegroundColor Green
        }
        
        # Remove obj folder
        $objPath = Join-Path $sample "obj"
        if (Test-Path $objPath) {
            Remove-Item -Path $objPath -Recurse -Force
            Write-Host "    ✓ Removed obj" -ForegroundColor Green
        }
    }
}

# Clean Orleans source bin/obj folders
Write-Host "`nCleaning Orleans source bin and obj folders..." -ForegroundColor Yellow
$sourcesToClean = @(
    "../../src/Orleans.Serialization",
    "../../src/Orleans.Serialization.Abstractions",
    "../../src/Orleans.Runtime",
    "../../src/Orleans.Core",
    "../../src/Orleans.Core.Abstractions",
    "../../src/Orleans.CodeGenerator"
)

foreach ($source in $sourcesToClean) {
    if (Test-Path $source) {
        Write-Host "  Cleaning $source..." -ForegroundColor Gray
        
        # Remove bin folder
        $binPath = Join-Path $source "bin"
        if (Test-Path $binPath) {
            Remove-Item -Path $binPath -Recurse -Force
            Write-Host "    ✓ Removed bin" -ForegroundColor Green
        }
        
        # Remove obj folder
        $objPath = Join-Path $source "obj"
        if (Test-Path $objPath) {
            Remove-Item -Path $objPath -Recurse -Force
            Write-Host "    ✓ Removed obj" -ForegroundColor Green
        }
    }
}

# Clean all .nupkg files
Write-Host "`nCleaning .nupkg files..." -ForegroundColor Yellow
$nupkgPaths = @(
    "../../Artifacts",
    "../Artifacts",
    "."
)

foreach ($path in $nupkgPaths) {
    if (Test-Path $path) {
        $nupkgFiles = Get-ChildItem -Path $path -Filter "*.nupkg" -Recurse -ErrorAction SilentlyContinue
        if ($nupkgFiles.Count -gt 0) {
            Write-Host "  Found $($nupkgFiles.Count) .nupkg files in $path" -ForegroundColor Gray
            foreach ($file in $nupkgFiles) {
                Remove-Item -Path $file.FullName -Force
                Write-Host "    ✓ Removed $($file.Name)" -ForegroundColor Green
            }
        }
    }
}

# Clean temp directories
Write-Host "`nCleaning temp directories..." -ForegroundColor Yellow
$tempDirs = Get-ChildItem -Path "." -Filter "temp-*" -Directory -ErrorAction SilentlyContinue
foreach ($dir in $tempDirs) {
    Remove-Item -Path $dir.FullName -Recurse -Force
    Write-Host "  ✓ Removed $($dir.Name)" -ForegroundColor Green
}

# Clean shims-proper directory
Write-Host "`nCleaning shims-proper directory..." -ForegroundColor Yellow
if (Test-Path "shims-proper") {
    $dllFiles = Get-ChildItem -Path "shims-proper" -Filter "*.dll" -ErrorAction SilentlyContinue
    foreach ($file in $dllFiles) {
        Remove-Item -Path $file.FullName -Force
        Write-Host "  ✓ Removed $($file.Name)" -ForegroundColor Green
    }
}

Write-Host "`n✓ All artifacts cleaned successfully!" -ForegroundColor Green
Write-Host "Ready for a fresh build." -ForegroundColor Cyan