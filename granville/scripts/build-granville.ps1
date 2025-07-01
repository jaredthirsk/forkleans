# PowerShell script to build Granville Orleans assemblies in correct order
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

Write-Host "Building Granville Orleans assemblies..." -ForegroundColor Green

# Clean previous builds
Write-Host "Cleaning previous builds..." -ForegroundColor Yellow
Get-ChildItem -Path "src" -Include "bin","obj" -Recurse -Directory | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

# Build projects in dependency order
$projects = @(
    "src/Orleans.Serialization.Abstractions/Orleans.Serialization.Abstractions.csproj",
    "src/Orleans.Serialization/Orleans.Serialization.csproj",
    "src/Orleans.Core.Abstractions/Orleans.Core.Abstractions.csproj",
    "src/Orleans.Core/Orleans.Core.csproj",
    "src/Orleans.CodeGenerator/Orleans.CodeGenerator.csproj",
    "src/Orleans.Analyzers/Orleans.Analyzers.csproj",
    "src/Orleans.Runtime/Orleans.Runtime.csproj"
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
        & dotnet build $project -c $Configuration --no-dependencies
        
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
        & dotnet build $project -c $Configuration
        
        if ($LASTEXITCODE -eq 0) {
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

# List all built assemblies
Write-Host "`nBuilt assemblies:" -ForegroundColor Yellow
Get-ChildItem -Path "src" -Filter "Granville.Orleans.*.dll" -Recurse | Where-Object { $_.DirectoryName -like "*\bin\*" } | ForEach-Object {
    Write-Host "  $($_.FullName)" -ForegroundColor Gray
}