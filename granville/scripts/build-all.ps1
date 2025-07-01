# Build-all.ps1 - Meta script to build all Granville Orleans components

param(
    [string]$Configuration = "Release",
    [switch]$SkipPackaging = $false,
    [switch]$SkipSamples = $false
)

$ErrorActionPreference = "Stop"

Write-Host "=== Granville Orleans Complete Build ===" -ForegroundColor Green
Write-Host "Configuration: $Configuration" -ForegroundColor Cyan

# Get the root directory
$rootDir = Join-Path $PSScriptRoot "../.."
Push-Location $rootDir

try {
    # Step 1: Clean previous builds
    Write-Host "`n=== Step 1: Cleaning previous builds ===" -ForegroundColor Yellow
    & "$PSScriptRoot/clean-build-artifacts.ps1"
    if ($LASTEXITCODE -ne 0) { throw "Clean failed" }

    # Step 2: Build Granville Orleans core assemblies
    Write-Host "`n=== Step 2: Building Granville Orleans assemblies ===" -ForegroundColor Yellow
    & "$PSScriptRoot/build-granville.ps1" -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Granville Orleans build failed" }

    # Step 3: Build Granville RPC
    Write-Host "`n=== Step 3: Building Granville RPC ===" -ForegroundColor Yellow
    & "$PSScriptRoot/build-granville-rpc.ps1"
    if ($LASTEXITCODE -ne 0) { throw "Granville RPC build failed" }

    # Step 4: Create NuGet packages (unless skipped)
    if (-not $SkipPackaging) {
        Write-Host "`n=== Step 4: Creating NuGet packages ===" -ForegroundColor Yellow
        
        Write-Host "Building Orleans packages..." -ForegroundColor Cyan
        & "$PSScriptRoot/build-orleans-packages.ps1"
        if ($LASTEXITCODE -ne 0) { throw "Orleans packaging failed" }
        
        Write-Host "Building RPC packages..." -ForegroundColor Cyan
        & "$PSScriptRoot/build-granville-rpc-packages.ps1"
        if ($LASTEXITCODE -ne 0) { throw "RPC packaging failed" }
    }
    else {
        Write-Host "`n=== Step 4: Skipping packaging (as requested) ===" -ForegroundColor Yellow
    }

    # Step 5: Build samples (unless skipped)
    if (-not $SkipSamples) {
        Write-Host "`n=== Step 5: Building Shooter sample ===" -ForegroundColor Yellow
        
        $samplesPath = Join-Path $rootDir "granville/samples/Rpc"
        Push-Location $samplesPath
        
        try {
            Write-Host "Building GranvilleSamples.sln..." -ForegroundColor Cyan
            & dotnet build GranvilleSamples.sln -c $Configuration
            if ($LASTEXITCODE -ne 0) { throw "Samples build failed" }
        }
        finally {
            Pop-Location
        }
    }
    else {
        Write-Host "`n=== Step 5: Skipping samples (as requested) ===" -ForegroundColor Yellow
    }

    # Step 6: Summary
    Write-Host "`n=== Build Complete! ===" -ForegroundColor Green
    Write-Host "Built components:" -ForegroundColor Cyan
    Write-Host "  ✓ Granville Orleans core assemblies" -ForegroundColor Gray
    Write-Host "  ✓ Granville RPC libraries" -ForegroundColor Gray
    
    if (-not $SkipPackaging) {
        Write-Host "  ✓ NuGet packages (in Artifacts/Release/)" -ForegroundColor Gray
    }
    
    if (-not $SkipSamples) {
        Write-Host "  ✓ Shooter sample application" -ForegroundColor Gray
    }
    
    Write-Host "`nNext steps:" -ForegroundColor Yellow
    Write-Host "  - To run the sample: cd granville/samples/Rpc/Shooter.AppHost && dotnet run" -ForegroundColor Gray
    Write-Host "  - To use packages: Setup local feed with ./granville/scripts/setup-local-feed.ps1" -ForegroundColor Gray
    
    exit 0
}
catch {
    Write-Host "`n=== Build Failed ===" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
finally {
    Pop-Location
}