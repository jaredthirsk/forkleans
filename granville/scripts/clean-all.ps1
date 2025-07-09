#!/usr/bin/env pwsh

# Comprehensive cleaning script for Granville Orleans
Write-Host "Cleaning Granville Orleans build artifacts..." -ForegroundColor Cyan

# Clean NuGet caches
Write-Host "`nClearing NuGet caches..." -ForegroundColor Yellow
if ($IsWindows -or $PSVersionTable.PSVersion.Major -lt 6) {
    & dotnet-win nuget locals all --clear
} else {
    & dotnet nuget locals all --clear
}

# Clean all bin and obj directories
Write-Host "`nCleaning bin and obj directories..." -ForegroundColor Yellow
Get-ChildItem -Path . -Include bin,obj -Recurse -Directory | ForEach-Object {
    Write-Host "  Removing: $($_.FullName)" -ForegroundColor Gray
    Remove-Item $_ -Recurse -Force -ErrorAction SilentlyContinue
}

# Clean temporary build directories
Write-Host "`nCleaning temporary directories..." -ForegroundColor Yellow
$tempDirs = @(
    "granville/compatibility-tools/temp-*",
    "granville/scripts/temp-*",
    "temp-*",
    "TestResults"
)

foreach ($pattern in $tempDirs) {
    Get-ChildItem -Path . -Filter $pattern -Recurse -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "  Removing: $($_.FullName)" -ForegroundColor Gray
        Remove-Item $_ -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# Clean logs
Write-Host "`nCleaning log files..." -ForegroundColor Yellow
$logPatterns = @(
    "*.log",
    "logs/*.log",
    "granville/samples/Rpc/logs/*.log"
)

foreach ($pattern in $logPatterns) {
    Get-ChildItem -Path . -Filter $pattern -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "  Removing: $($_.FullName)" -ForegroundColor Gray
        Remove-Item $_ -Force -ErrorAction SilentlyContinue
    }
}

# Clean Artifacts/Release if requested
if ($args -contains "--artifacts") {
    Write-Host "`nCleaning Artifacts/Release..." -ForegroundColor Yellow
    Remove-Item -Path "Artifacts/Release/*" -Force -ErrorAction SilentlyContinue
}

Write-Host "`n✓ Cleaning complete!" -ForegroundColor Green
Write-Host "Tip: Use --artifacts flag to also clean Artifacts/Release directory" -ForegroundColor Cyan