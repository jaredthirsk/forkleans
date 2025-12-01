#!/usr/bin/env pwsh
# PowerShell script to build ALL Granville Orleans assemblies needed by Shooter sample
param(
    [string]$Configuration = "Release",
    [switch]$SkipClean = $false
)

$ErrorActionPreference = "Stop"

# Ensure we're in the repository root
$repoRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName
Push-Location $repoRoot

Write-Host "Building ALL Granville Orleans assemblies..." -ForegroundColor Green

# Determine if we're running in a container (Docker/devcontainer)
$isContainer = Test-Path "/.dockerenv"

# Determine if we're running in WSL2 (but not in a container)
$isWSL = $false
if (-not $isContainer -and (Test-Path "/proc/version")) {
    $procVersion = Get-Content "/proc/version" -ErrorAction SilentlyContinue
    if ($procVersion -match "(WSL|Microsoft)") {
        $isWSL = $true
    }
}

# Choose appropriate dotnet command
# In containers, always use native dotnet; in WSL2 (not container), use dotnet-win
$dotnetCmd = if ($isContainer) { "dotnet" } elseif ($isWSL) { "dotnet-win" } else { "dotnet" }

# Clean previous builds
if (-not $SkipClean) {
    Write-Host "Cleaning previous builds..." -ForegroundColor Yellow
    Get-ChildItem -Path "src" -Include "bin","obj" -Recurse -Directory | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
} else {
    Write-Host "Skipping clean step" -ForegroundColor Cyan
}

# Build projects in dependency order
$projects = @(
    # Code generation and analyzers must be built first
    "src/Orleans.CodeGenerator/Orleans.CodeGenerator.csproj",
    "src/Orleans.Analyzers/Orleans.Analyzers.csproj",
    
    # Core serialization
    "src/Orleans.Serialization.Abstractions/Orleans.Serialization.Abstractions.csproj",
    "src/Orleans.Serialization/Orleans.Serialization.csproj",
    
    # Core abstractions and implementations
    "src/Orleans.Core.Abstractions/Orleans.Core.Abstractions.csproj",
    "src/Orleans.Core/Orleans.Core.csproj",
    
    # SDK
    "src/Orleans.Sdk/Orleans.Sdk.csproj",
    
    # Runtime
    "src/Orleans.Runtime/Orleans.Runtime.csproj",
    
    # Client and Server
    "src/Orleans.Client/Orleans.Client.csproj",
    "src/Orleans.Server/Orleans.Server.csproj"
)

# Function to create compatibility symlinks
function Create-CompatibilityLinks {
    param($ProjectDir, $Configuration)

    $binPath = Join-Path $ProjectDir "bin\$Configuration"

    Get-ChildItem -Path $binPath -Filter "Granville.Orleans.*.dll" -Recurse | ForEach-Object {
        $granvilleName = $_.Name
        $orleansName = $granvilleName -replace "^Granville\.", ""
        $targetPath = Join-Path $_.DirectoryName $orleansName

        # Create a copy with the old name for compatibility
        if (-not (Test-Path $targetPath)) {
            Copy-Item $_.FullName $targetPath
            Write-Host "  Created compatibility copy: $orleansName" -ForegroundColor DarkGray
        }
    }
}

# Build each project
foreach ($project in $projects) {
    Write-Host "`nBuilding $project..." -ForegroundColor Cyan

    try {
        & $dotnetCmd build $project -c $Configuration -p:BuildAsGranville=true -p:EnableGranvilleCodeGen=true -p:Orleans_DesignTimeBuild=false --no-dependencies
        
        if ($LASTEXITCODE -ne 0) {
            throw "Build failed for $project"
        }

        # Create compatibility links after successful build
        $projectDir = Split-Path $project -Parent
        Create-CompatibilityLinks -ProjectDir $projectDir -Configuration $Configuration

        Write-Host "✓ Successfully built $project" -ForegroundColor Green
    }
    catch {
        Write-Host "✗ Failed to build $project" -ForegroundColor Red
        Write-Host $_.Exception.Message -ForegroundColor Red

        # Try alternative approach: build with dependencies
        Write-Host "  Retrying with dependencies..." -ForegroundColor Yellow
        & $dotnetCmd build $project -c $Configuration -p:BuildAsGranville=true

        if ($LASTEXITCODE -eq 0) {
            $projectDir = Split-Path $project -Parent
            Create-CompatibilityLinks -ProjectDir $projectDir -Configuration $Configuration
            Write-Host "✓ Successfully built $project (with dependencies)" -ForegroundColor Green
        }
        else {
            Write-Host "✗ Build still failed" -ForegroundColor Red
            exit 1
        }
    }
}

Write-Host "`n=== Build Summary ===" -ForegroundColor Green
Write-Host "All Granville Orleans assemblies have been built successfully!" -ForegroundColor Green
Write-Host "Assemblies are named Granville.Orleans.* with compatibility copies as Orleans.*" -ForegroundColor Cyan

# Note: Package dependency fixing moved to build-all-granville.ps1 after packaging step

# List all built assemblies
Write-Host "`nBuilt assemblies:" -ForegroundColor Yellow
Get-ChildItem -Path "src" -Filter "Granville.Orleans.*.dll" -Recurse | Where-Object { $_.DirectoryName -like "*\bin\*" } | ForEach-Object {
    Write-Host "  $($_.FullName)" -ForegroundColor Gray
}

# Restore original location
Pop-Location