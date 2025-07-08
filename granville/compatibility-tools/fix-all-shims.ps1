#!/usr/bin/env pwsh

$ErrorActionPreference = "Stop"

Write-Host "=== Comprehensive Shim Fixing Script ===" -ForegroundColor Cyan
Write-Host "This script will rebuild all shims to ensure proper type forwarding" -ForegroundColor Yellow

# Step 1: Clean and rebuild all Granville Orleans assemblies
Write-Host "`n1. Rebuilding Granville Orleans assemblies..." -ForegroundColor Green
Push-Location "../.."
try {
    & "./granville/scripts/build-granville.ps1"
} finally {
    Pop-Location
}
if ($LASTEXITCODE -ne 0) {
    Write-Host "✗ Failed to build Granville assemblies" -ForegroundColor Red
    exit 1
}

# Step 2: Generate comprehensive shim files with all type forwards
Write-Host "`n2. Generating comprehensive shim files..." -ForegroundColor Green

# List of assemblies to create shims for
$assemblies = @(
    "Orleans.Core.Abstractions",
    "Orleans.Core", 
    "Orleans.Runtime",
    "Orleans.Serialization.Abstractions",
    "Orleans.Serialization",
    "Orleans.CodeGenerator",
    "Orleans.Analyzers",
    "Orleans.Sdk",
    "Orleans.Client",
    "Orleans.Server",
    "Orleans.Reminders",
    "Orleans.Persistence.Memory",
    "Orleans.Serialization.SystemTextJson"
)

foreach ($assembly in $assemblies) {
    Write-Host "Processing $assembly..." -ForegroundColor Yellow
    
    # Generate shim with all types from the corresponding Granville assembly
    & "./generate-comprehensive-shim.ps1" -AssemblyName $assembly
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "✗ Failed to generate shim for $assembly" -ForegroundColor Red
        exit 1
    }
}

# Step 3: Compile all shims
Write-Host "`n3. Compiling all shims..." -ForegroundColor Green
& "./compile-all-shims.ps1"
if ($LASTEXITCODE -ne 0) {
    Write-Host "✗ Failed to compile shims" -ForegroundColor Red
    exit 1
}

# Step 4: Create NuGet packages for all shims
Write-Host "`n4. Creating NuGet packages..." -ForegroundColor Green
& "./create-all-shim-packages.ps1"
if ($LASTEXITCODE -ne 0) {
    Write-Host "✗ Failed to create NuGet packages" -ForegroundColor Red
    exit 1
}

# Step 5: Clear NuGet cache and restore
Write-Host "`n5. Clearing NuGet cache and restoring..." -ForegroundColor Green
dotnet-win nuget locals all --clear
Push-Location "../samples/Rpc"
try {
    dotnet-win restore
    if ($LASTEXITCODE -ne 0) {
        Write-Host "✗ Failed to restore packages" -ForegroundColor Red
        exit 1
    }
} finally {
    Pop-Location
}

Write-Host "`n✓ All shims have been comprehensively fixed!" -ForegroundColor Green
Write-Host "You can now test the Shooter.Silo application" -ForegroundColor Cyan