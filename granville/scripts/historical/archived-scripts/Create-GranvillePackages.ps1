#!/usr/bin/pwsh
# Create-GranvillePackages.ps1
# Script to create NuGet packages for Granville projects and publish to local feed

param(
    [Parameter(Mandatory=$false)]
    [string]$LocalFeedPath = "$PSScriptRoot/../../../Artifacts/Release",

    [Parameter()]
    [string]$Configuration = "Release",

    [Parameter()]
    [string]$VersionSuffix = "alpha",

    [Parameter()]
    [switch]$SkipBuild = $false,

    [Parameter()]
    [switch]$SkipPublish = $false,

    [Parameter()]
    [ValidateSet("Essential", "RpcTypical", "All")]
    [string]$Mode = "RpcTypical"
)

$ErrorActionPreference = "Stop"

# Create local feed directory if it doesn't exist
if (-not (Test-Path $LocalFeedPath)) {
    Write-Host "Creating local NuGet feed directory: $LocalFeedPath" -ForegroundColor Green
    New-Item -ItemType Directory -Path $LocalFeedPath -Force | Out-Null
}

# Essential packages - minimal set needed for basic RPC functionality
$essentialPackages = @(
    # Core abstractions and runtime (in dependency order)
    "src/Orleans.Serialization.Abstractions/Orleans.Serialization.Abstractions.csproj",
    "src/Orleans.Core.Abstractions/Orleans.Core.Abstractions.csproj",
    "src/Orleans.Analyzers/Orleans.Analyzers.csproj",
    "src/Orleans.CodeGenerator/Orleans.CodeGenerator.csproj",
    "src/Orleans.Serialization/Orleans.Serialization.csproj",
    "src/Orleans.Core/Orleans.Core.csproj",
    "src/Orleans.Sdk/Orleans.Sdk.csproj",
    "src/Orleans.Runtime/Orleans.Runtime.csproj",
    "src/Orleans.Client/Orleans.Client.csproj",
    "src/Orleans.Server/Orleans.Server.csproj",
    "src/Orleans.Persistence.Memory/Orleans.Persistence.Memory.csproj"
)

# Core packages that RPC depends on or are commonly needed (excluding clustering)
$corePackages = @(
    # Common extensions (non-clustering)
    "src/Orleans.EventSourcing/Orleans.EventSourcing.csproj",
    "src/Orleans.Reminders/Orleans.Reminders.csproj",
    "src/Orleans.Streaming/Orleans.Streaming.csproj",
    "src/Orleans.Transactions/Orleans.Transactions.csproj",
    "src/Orleans.Connections.Security/Orleans.Connections.Security.csproj",

    # Testing support
    "src/Orleans.TestingHost/Orleans.TestingHost.csproj"
)

# RPC packages
$rpcPackages = @(
    "src/Rpc/Orleans.Rpc.Abstractions/Orleans.Rpc.Abstractions.csproj",
    "src/Rpc/Orleans.Rpc.Sdk/Orleans.Rpc.Sdk.csproj",
    "src/Rpc/Orleans.Rpc.Client/Orleans.Rpc.Client.csproj",
    "src/Rpc/Orleans.Rpc.Server/Orleans.Rpc.Server.csproj",
    "src/Rpc/Orleans.Rpc.Transport.LiteNetLib/Orleans.Rpc.Transport.LiteNetLib.csproj",
    "src/Rpc/Orleans.Rpc.Transport.Ruffles/Orleans.Rpc.Transport.Ruffles.csproj"
)

# Optional packages (included with -IncludeAllPackages)
# Note: Clustering packages are excluded as they're not used in the fork
$optionalPackages = @(
    # Azure providers (non-clustering)
    "src/Azure/Orleans.Persistence.AzureStorage/Orleans.Persistence.AzureStorage.csproj",
    "src/Azure/Orleans.Reminders.AzureStorage/Orleans.Reminders.AzureStorage.csproj",
    "src/Azure/Orleans.Streaming.AzureStorage/Orleans.Streaming.AzureStorage.csproj",
    "src/Azure/Orleans.Streaming.EventHubs/Orleans.Streaming.EventHubs.csproj",

    # ADO.NET providers (non-clustering)
    "src/AdoNet/Orleans.Persistence.AdoNet/Orleans.Persistence.AdoNet.csproj",
    "src/AdoNet/Orleans.Reminders.AdoNet/Orleans.Reminders.AdoNet.csproj",

    # AWS providers (non-clustering)
    "src/AWS/Orleans.Persistence.DynamoDB/Orleans.Persistence.DynamoDB.csproj",

    # Redis providers (non-clustering)
    "src/Redis/Orleans.Persistence.Redis/Orleans.Persistence.Redis.csproj",

    # Other extensions
    "src/Orleans.BroadcastChannel/Orleans.BroadcastChannel.csproj",
    "src/Orleans.Hosting.Kubernetes/Orleans.Hosting.Kubernetes.csproj",
    "src/Orleans.Clustering.ZooKeeper/Orleans.Clustering.ZooKeeper.csproj",
    "src/Orleans.Clustering.Consul/Orleans.Clustering.Consul.csproj",
    "src/Orleans.Journaling/Orleans.Journaling.csproj"
)

# Combine package lists based on mode
switch ($Mode) {
    "Essential" {
        $projectsToPack = $essentialPackages + $rpcPackages
        Write-Host "Mode: Essential - Building minimal set for RPC functionality" -ForegroundColor Yellow
    }
    "RpcTypical" {
        $projectsToPack = $essentialPackages + $corePackages + $rpcPackages
        Write-Host "Mode: RpcTypical - Building typical packages for Granville RPC" -ForegroundColor Yellow
    }
    "All" {
        $projectsToPack = $essentialPackages + $corePackages + $rpcPackages + $optionalPackages
        Write-Host "Mode: All - Building all available packages" -ForegroundColor Yellow
    }
}

Write-Host "Planning to create $($projectsToPack.Count) packages" -ForegroundColor Cyan

# Build all projects first (unless skipped)
if (-not $SkipBuild) {
    Write-Host "`nBuilding projects in $Configuration configuration..." -ForegroundColor Cyan

    # For 'All' mode, build the entire solution (faster due to parallelization)
    # For other modes, build only the specific projects
    if ($Mode -eq "All") {
        Write-Host "Building in two phases to handle dependencies..." -ForegroundColor Gray
        
        # Phase 1: Build non-sample projects (core libraries)
        Write-Host "`nPhase 1: Building core libraries..." -ForegroundColor Cyan
        $coreSolution = "Orleans.sln"  # This excludes the RPC samples
        
        $buildArgs = @(
            "build",
            $coreSolution,
            "-c", $Configuration,
            "--verbosity", "minimal"
        )
        
        Write-Host "Building $coreSolution..." -ForegroundColor Gray
        $buildOutput = & dotnet $buildArgs 2>&1
        
        if ($LASTEXITCODE -ne 0) {
            Write-Host "Build failed for $coreSolution" -ForegroundColor Red
            Write-Host "Error details:" -ForegroundColor Red
            Write-Host $buildOutput -ForegroundColor Red
            throw "Build failed"
        }
        
        Write-Host "Core libraries built successfully" -ForegroundColor Green
    }
    else {
        # Build only the projects we're going to pack
        Write-Host "Building $($projectsToPack.Count) projects that will be packed..." -ForegroundColor Gray

        $buildFailed = $false
        $failedBuilds = @()

        foreach ($project in $projectsToPack) {
            if (-not (Test-Path $project)) {
                Write-Warning "Project not found: $project"
                continue
            }

            $projectName = [System.IO.Path]::GetFileNameWithoutExtension($project)
            Write-Host "Building $projectName..." -ForegroundColor Gray

            $buildArgs = @(
                "build",
                $project,
                "-c", $Configuration,
                "--verbosity", "minimal"
            )

            $buildOutput = & dotnet $buildArgs 2>&1

            if ($LASTEXITCODE -ne 0) {
                Write-Warning "Build failed for $project"
                Write-Host "Error details:" -ForegroundColor Red
                Write-Host $buildOutput -ForegroundColor Red
                $buildFailed = $true
                $failedBuilds += $project
            }
        }

        if ($buildFailed) {
            Write-Host "`nBuild failed for $($failedBuilds.Count) projects:" -ForegroundColor Red
            foreach ($failed in $failedBuilds) {
                Write-Host "  - $failed" -ForegroundColor Red
            }
            throw "Build failed"
        }

        Write-Host "`nAll projects built successfully" -ForegroundColor Green
    }
}

# Clean artifacts directory - use the standard location from Directory.Build.props
$artifactsPath = Join-Path $PSScriptRoot "Artifacts/$Configuration"
if (Test-Path $artifactsPath) {
    Write-Host "Cleaning existing packages in $artifactsPath..." -ForegroundColor Gray
    Remove-Item -Path "$artifactsPath/*.nupkg" -Force -ErrorAction SilentlyContinue
} else {
    New-Item -ItemType Directory -Path $artifactsPath -Force | Out-Null
}

# Package each project
Write-Host "`nCreating NuGet packages..." -ForegroundColor Cyan

$packagesCreated = @()
$failedPackages = @()
$retriedPackages = @()

foreach ($project in $projectsToPack) {
    if (-not (Test-Path $project)) {
        Write-Warning "Project not found: $project"
        continue
    }

    Write-Host "Packaging $project..." -ForegroundColor Gray

    $packArgs = @(
        "pack",
        $project,
        "-c", $Configuration,
        "--no-build",  # We already built
        "-o", $artifactsPath
    )

    if ($VersionSuffix) {
        $packArgs += "--version-suffix"
        $packArgs += $VersionSuffix
    }

    $output = & dotnet $packArgs 2>&1

    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Pack failed for $project with --no-build, retrying with build..."

        # First show the initial error to help diagnose
        Write-Host "Initial pack error:" -ForegroundColor Yellow
        Write-Host $output -ForegroundColor Yellow

        # Retry without --no-build flag
        $retryArgs = $packArgs | Where-Object { $_ -ne "--no-build" }
        Write-Host "Retrying with: dotnet $($retryArgs -join ' ')" -ForegroundColor Gray
        $retryOutput = & dotnet $retryArgs 2>&1

        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Pack failed for $project even with build"
            Write-Host "Build/Pack error details:" -ForegroundColor Red
            Write-Host $retryOutput -ForegroundColor Red
            $failedPackages += $project
            continue
        }
        else {
            Write-Host "Pack succeeded after retry with build" -ForegroundColor Green
            $output = $retryOutput
            $retriedPackages += $project
        }
    }

    # Find the created package
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($project)
    # Convert Orleans.* to Granville.* for package name
    $packageName = $projectName -replace "^Orleans\.", "Granville."
    $packagePattern = "$packageName.*.nupkg"
    $package = Get-ChildItem -Path $artifactsPath -Filter $packagePattern | Sort-Object LastWriteTime -Descending | Select-Object -First 1

    if ($package) {
        $packagesCreated += $package
    }
}

# Publish to local feed (unless skipped)
if (-not $SkipPublish -and $packagesCreated.Count -gt 0) {
    Write-Host "`nPublishing packages to local feed: $LocalFeedPath" -ForegroundColor Cyan

    foreach ($package in $packagesCreated) {
        Write-Host "Publishing $($package.Name)..." -ForegroundColor Gray

        # Copy to local feed
        Copy-Item -Path $package.FullName -Destination $LocalFeedPath -Force
    }

    Write-Host "`nPackages published successfully!" -ForegroundColor Green
    Write-Host "Total packages: $($packagesCreated.Count)" -ForegroundColor Green
}

# Phase 2 for 'All' mode: Build RPC samples after packages are available
if ($Mode -eq "All" -and -not $SkipBuild) {
    Write-Host "`nPhase 2: Building RPC sample projects..." -ForegroundColor Cyan
    
    $samplesSolution = "samples/Rpc/GranvilleSamples.sln"
    if (Test-Path $samplesSolution) {
        Write-Host "Building $samplesSolution..." -ForegroundColor Gray
        
        $buildArgs = @(
            "build",
            $samplesSolution,
            "-c", $Configuration,
            "--verbosity", "minimal"
        )
        
        $buildOutput = & dotnet $buildArgs 2>&1
        
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Build failed for RPC samples"
            Write-Host "Error details:" -ForegroundColor Red
            Write-Host $buildOutput -ForegroundColor Red
            Write-Warning "Samples failed to build, but packages were created successfully"
        } else {
            Write-Host "RPC samples built successfully" -ForegroundColor Green
        }
    } else {
        Write-Warning "RPC samples solution not found at $samplesSolution"
    }
}

# Display summary
Write-Host "`n=== Summary ===" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor Gray
Write-Host "Version Suffix: $(if ($VersionSuffix) { $VersionSuffix } else { 'none' })" -ForegroundColor Gray
Write-Host "Local Feed: $LocalFeedPath" -ForegroundColor Gray
Write-Host "Packages Created: $($packagesCreated.Count)" -ForegroundColor Gray
if ($retriedPackages.Count -gt 0) {
    Write-Host "Packages Requiring Build Retry: $($retriedPackages.Count)" -ForegroundColor Yellow
}
if ($failedPackages.Count -gt 0) {
    Write-Host "Failed Packages: $($failedPackages.Count)" -ForegroundColor Red
    foreach ($failed in $failedPackages) {
        Write-Host "  - $failed" -ForegroundColor Red
    }
}

Write-Host "`nPackages:" -ForegroundColor Gray
foreach ($package in $packagesCreated) {
    Write-Host "  ✓ $($package.Name)" -ForegroundColor Green
}

if ($retriedPackages.Count -gt 0) {
    Write-Host "`nPackages that required retry with build:" -ForegroundColor Yellow
    foreach ($retried in $retriedPackages) {
        Write-Host "  ⚠ $retried" -ForegroundColor Yellow
    }
    Write-Host "`nNote: These packages may need their output paths configured or build dependencies resolved." -ForegroundColor Yellow
}

# Generate NuGet.config content
$nugetConfigContent = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <!-- Local Granville feed -->
    <add key="LocalGranville" value="$LocalFeedPath" />
    <!-- Default NuGet feed -->
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>

  <packageSourceMapping>
    <!-- Map all Granville packages to local feed -->
    <packageSource key="LocalGranville">
      <package pattern="Granville.*" />
    </packageSource>
    <!-- Everything else from nuget.org -->
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@

# Save NuGet.config template
$nugetConfigPath = Join-Path $PSScriptRoot "NuGet.config.local"
$nugetConfigContent | Set-Content -Path $nugetConfigPath -Encoding UTF8

# Instructions for consuming the packages
Write-Host "`n=== Next Steps ===" -ForegroundColor Yellow
Write-Host "1. Add the local feed to your NuGet sources:" -ForegroundColor Yellow
Write-Host "   dotnet nuget add source `"$LocalFeedPath`" -n `"LocalGranville`"" -ForegroundColor White
Write-Host ""
Write-Host "2. Or copy the generated NuGet.config.local to your consuming project as NuGet.config" -ForegroundColor Yellow
Write-Host "   Generated at: $nugetConfigPath" -ForegroundColor Gray
Write-Host ""
Write-Host "3. Reference the packages in your project:" -ForegroundColor Yellow
Write-Host "   dotnet add package Granville.Rpc.Client --prerelease" -ForegroundColor White
Write-Host "   dotnet add package Granville.Rpc.Transport.LiteNetLib --prerelease" -ForegroundColor White
Write-Host "   dotnet add package Granville.Rpc.Sdk --prerelease" -ForegroundColor White
Write-Host ""
Write-Host "4. For a minimal RPC setup, you typically need:" -ForegroundColor Yellow
Write-Host "   - Granville.Rpc.Client (for client applications)" -ForegroundColor Gray
Write-Host "   - Granville.Rpc.Server (for server applications)" -ForegroundColor Gray
Write-Host "   - Granville.Rpc.Sdk (for code generation)" -ForegroundColor Gray
Write-Host "   - One transport package (LiteNetLib or Ruffles)" -ForegroundColor Gray

# Exit with error code if any packages failed
if ($failedPackages.Count -gt 0) {
    exit 1
}
