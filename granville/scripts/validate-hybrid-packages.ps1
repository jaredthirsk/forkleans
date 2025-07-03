#!/usr/bin/env pwsh
# Validates the hybrid package configuration for Granville Orleans
param(
    [string]$LocalFeedPath = ".\Artifacts\Release",
    [switch]$Verbose
)

$ErrorActionPreference = "Stop"

Write-Host "=== Validating Hybrid Package Configuration ===" -ForegroundColor Green
Write-Host "Local feed path: $LocalFeedPath" -ForegroundColor Cyan

# Define package categories based on hybrid strategy
$modifiedAssemblies = @(
    "Orleans.Core",
    "Orleans.Core.Abstractions",
    "Orleans.Runtime",
    "Orleans.Serialization",
    "Orleans.Serialization.Abstractions"
)

$unmodifiedAssemblies = @(
    "Orleans.Client",
    "Orleans.Server",
    "Orleans.Reminders",
    "Orleans.Reminders.Abstractions",
    "Orleans.Serialization.SystemTextJson",
    "Orleans.Persistence.Memory"
)

$validationResults = @{
    Success = $true
    Issues = @()
}

function Add-Issue {
    param($Message, $Severity = "Error")
    $validationResults.Issues += @{
        Message = $Message
        Severity = $Severity
    }
    if ($Severity -eq "Error") {
        $validationResults.Success = $false
    }
}

# Check 1: Verify local feed exists
Write-Host "`nChecking local feed..." -ForegroundColor Yellow
if (-not (Test-Path $LocalFeedPath)) {
    Add-Issue "Local feed path '$LocalFeedPath' does not exist"
} else {
    Write-Host "  ✓ Local feed exists" -ForegroundColor Green
}

# Check 2: Verify Granville.Orleans packages exist for modified assemblies
Write-Host "`nChecking Granville.Orleans packages (modified assemblies)..." -ForegroundColor Yellow
foreach ($assembly in $modifiedAssemblies) {
    $packagePattern = Join-Path $LocalFeedPath "Granville.$assembly.*.nupkg"
    $packages = Get-ChildItem -Path $packagePattern -ErrorAction SilentlyContinue
    
    if ($packages.Count -eq 0) {
        Add-Issue "Missing Granville.$assembly package"
        Write-Host "  ✗ Granville.$assembly - NOT FOUND" -ForegroundColor Red
    } else {
        $latestPackage = $packages | Sort-Object Name -Descending | Select-Object -First 1
        Write-Host "  ✓ Granville.$assembly - $($latestPackage.Name)" -ForegroundColor Green
    }
}

# Check 3: Verify shim packages exist for modified assemblies
Write-Host "`nChecking Microsoft.Orleans shim packages..." -ForegroundColor Yellow
foreach ($assembly in $modifiedAssemblies) {
    $shimPattern = Join-Path $LocalFeedPath "Microsoft.$assembly.*-granville-shim.nupkg"
    $shims = Get-ChildItem -Path $shimPattern -ErrorAction SilentlyContinue
    
    if ($shims.Count -eq 0) {
        Add-Issue "Missing Microsoft.$assembly shim package"
        Write-Host "  ✗ Microsoft.$assembly (shim) - NOT FOUND" -ForegroundColor Red
    } else {
        $latestShim = $shims | Sort-Object Name -Descending | Select-Object -First 1
        Write-Host "  ✓ Microsoft.$assembly (shim) - $($latestShim.Name)" -ForegroundColor Green
    }
}

# Check 4: Verify NO Granville packages for unmodified assemblies
Write-Host "`nChecking for unnecessary Granville packages..." -ForegroundColor Yellow
$unnecessaryPackagesFound = $false
foreach ($assembly in $unmodifiedAssemblies) {
    $packagePattern = Join-Path $LocalFeedPath "Granville.$assembly.*.nupkg"
    $packages = Get-ChildItem -Path $packagePattern -ErrorAction SilentlyContinue
    
    if ($packages.Count -gt 0) {
        Add-Issue "Unnecessary Granville.$assembly package found (should use official Microsoft package)" "Warning"
        Write-Host "  ! Granville.$assembly - UNNECESSARY (use official package)" -ForegroundColor Yellow
        $unnecessaryPackagesFound = $true
    }
}
if (-not $unnecessaryPackagesFound) {
    Write-Host "  ✓ No unnecessary Granville packages found" -ForegroundColor Green
}

# Check 5: Verify Granville.Rpc packages
Write-Host "`nChecking Granville.Rpc packages..." -ForegroundColor Yellow
$rpcPackages = @(
    "Granville.Rpc.Abstractions",
    "Granville.Rpc.Client",
    "Granville.Rpc.Server",
    "Granville.Rpc.Sdk"
)

foreach ($package in $rpcPackages) {
    $packagePattern = Join-Path $LocalFeedPath "$package.*.nupkg"
    $packages = Get-ChildItem -Path $packagePattern -ErrorAction SilentlyContinue
    
    if ($packages.Count -eq 0) {
        Add-Issue "Missing $package package"
        Write-Host "  ✗ $package - NOT FOUND" -ForegroundColor Red
    } else {
        $latestPackage = $packages | Sort-Object Name -Descending | Select-Object -First 1
        Write-Host "  ✓ $package - $($latestPackage.Name)" -ForegroundColor Green
    }
}

# Check 6: Verify NuGet configuration
Write-Host "`nChecking NuGet configuration..." -ForegroundColor Yellow
$nugetConfigPaths = @(
    ".\NuGet.config",
    ".\granville\samples\Rpc\NuGet.config"
)

foreach ($configPath in $nugetConfigPaths) {
    if (Test-Path $configPath) {
        $content = Get-Content $configPath -Raw
        if ($content -match "nuget\.org" -and $content -match "local|Artifacts") {
            Write-Host "  ✓ $configPath - Contains both nuget.org and local feed" -ForegroundColor Green
        } else {
            Add-Issue "$configPath missing nuget.org or local feed configuration" "Warning"
            Write-Host "  ! $configPath - Missing required feed configuration" -ForegroundColor Yellow
        }
    }
}

# Summary
Write-Host "`n=== Validation Summary ===" -ForegroundColor Cyan

if ($validationResults.Success) {
    Write-Host "✓ All critical checks passed!" -ForegroundColor Green
    Write-Host "`nHybrid package configuration is ready to use." -ForegroundColor Green
} else {
    Write-Host "✗ Validation failed with errors" -ForegroundColor Red
}

if ($validationResults.Issues.Count -gt 0) {
    Write-Host "`nIssues found:" -ForegroundColor Yellow
    foreach ($issue in $validationResults.Issues) {
        $color = if ($issue.Severity -eq "Error") { "Red" } else { "Yellow" }
        Write-Host "  [$($issue.Severity)] $($issue.Message)" -ForegroundColor $color
    }
}

# Provide recommendations
Write-Host "`n=== Recommendations ===" -ForegroundColor Cyan
Write-Host "For official Microsoft packages, ensure your project references nuget.org" -ForegroundColor Gray
Write-Host "Official packages needed:" -ForegroundColor Gray
foreach ($assembly in $unmodifiedAssemblies) {
    Write-Host "  - Microsoft.$assembly (9.1.2)" -ForegroundColor Gray
}

if (-not $validationResults.Success) {
    Write-Host "`nTo fix issues:" -ForegroundColor Yellow
    Write-Host "1. Run ./granville/scripts/build-orleans-packages.ps1" -ForegroundColor Gray
    Write-Host "2. Run ./granville/compatibility-tools/Generate-TypeForwardingShims.ps1" -ForegroundColor Gray
    Write-Host "3. Ensure NuGet.config includes both nuget.org and local feed" -ForegroundColor Gray
    
    exit 1
}

exit 0