#!/usr/bin/env pwsh
# Build Orleans shim assemblies with metadata providers for Granville compatibility

param(
    [string]$Configuration = "Release",
    [string]$OutputPath = "$PSScriptRoot/shims-with-metadata"
)

$ErrorActionPreference = "Stop"

Write-Host "Building Orleans shims with metadata providers..." -ForegroundColor Green

# Create output directory
if (Test-Path $OutputPath) {
    Remove-Item $OutputPath -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null

# Get the root directory
$repoRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName

# Build Granville assemblies first (we need them for the shims to reference)
Write-Host "Building Granville assemblies first..." -ForegroundColor Yellow
& "$repoRoot/granville/scripts/build-granville-orleans.ps1" -Configuration $Configuration

# Function to compile a shim
function Build-Shim {
    param(
        [string]$SourceFile,
        [string]$AssemblyName,
        [string[]]$References
    )
    
    Write-Host "Building $AssemblyName..." -ForegroundColor Cyan
    
    $outputDll = Join-Path $OutputPath "$AssemblyName.dll"
    
    # Build reference list
    $referenceArgs = @()
    foreach ($ref in $References) {
        if ($ref -like "*.dll") {
            $referenceArgs += "-r:$ref"
        } else {
            $referenceArgs += "-r:$ref"
        }
    }
    
    # Add Granville assemblies to reference path
    $granvilleAssembliesPath = "$repoRoot/Artifacts/$Configuration"
    $referenceArgs += "-r:$granvilleAssembliesPath/Granville.Orleans.Core.Abstractions.dll"
    $referenceArgs += "-r:$granvilleAssembliesPath/Granville.Orleans.Core.dll"
    $referenceArgs += "-r:$granvilleAssembliesPath/Granville.Orleans.Serialization.dll"
    $referenceArgs += "-r:$granvilleAssembliesPath/Granville.Orleans.Serialization.Abstractions.dll"
    
    # Add Orleans ApplicationPart reference
    $referenceArgs += "-r:$granvilleAssembliesPath/Orleans.dll"
    
    # Add system references
    $referenceArgs += "-r:System.Runtime.dll"
    $referenceArgs += "-r:System.Collections.dll"
    $referenceArgs += "-r:Microsoft.Extensions.Options.dll"
    
    # Compile
    $compilerArgs = @(
        "-target:library",
        "-out:$outputDll",
        "-nostdlib",
        "-noconfig",
        "-optimize+",
        "-debug:portable",
        "-define:RELEASE"
    ) + $referenceArgs + @($SourceFile)
    
    # Find the C# compiler
    $csc = Get-Command csc -ErrorAction SilentlyContinue
    if (-not $csc) {
        # Try to find it in .NET SDK
        $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
        if ($dotnet) {
            $sdkPath = & $dotnet --list-sdks | Select-Object -First 1 | ForEach-Object { ($_ -split ' ')[1].Trim('[',']') }
            $cscPath = Join-Path $sdkPath "Roslyn/bincore/csc.dll"
            if (Test-Path $cscPath) {
                & $dotnet $cscPath $compilerArgs
            } else {
                throw "C# compiler not found"
            }
        } else {
            throw "C# compiler not found"
        }
    } else {
        & $csc $compilerArgs
    }
    
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to compile $AssemblyName"
    }
    
    Write-Host "Successfully built $AssemblyName" -ForegroundColor Green
}

# Build each shim with metadata
$shimsPath = "$PSScriptRoot/shims-proper"

Build-Shim -SourceFile "$shimsPath/Orleans.Core.Abstractions-WithMetadata.cs" `
           -AssemblyName "Orleans.Core.Abstractions" `
           -References @()

Build-Shim -SourceFile "$shimsPath/Orleans.Core-WithMetadata.cs" `
           -AssemblyName "Orleans.Core" `
           -References @()

Build-Shim -SourceFile "$shimsPath/Orleans.Serialization-WithMetadata.cs" `
           -AssemblyName "Orleans.Serialization" `
           -References @()

Write-Host "`nShims with metadata providers built successfully!" -ForegroundColor Green
Write-Host "Output: $OutputPath" -ForegroundColor Yellow