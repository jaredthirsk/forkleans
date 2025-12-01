#!/usr/bin/env pwsh

# Ensure we're in the right directory
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

# Create shims-proper directory if it doesn't exist
if (!(Test-Path "shims-proper")) {
    New-Item -ItemType Directory -Path "shims-proper" | Out-Null
}

# Detect if we're running in a container (Docker/devcontainer)
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

# Build the type-forwarding-generator tool if needed
if (!(Test-Path "type-forwarding-generator/bin/Release/net9.0/GenerateTypeForwardingAssemblies.dll")) {
    Write-Host "Building type-forwarding-generator tool..." -ForegroundColor Yellow
    Push-Location type-forwarding-generator
    & $dotnetCmd build -c Release
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to build type-forwarding-generator"
        Pop-Location
        exit 1
    }
    Pop-Location
    Write-Host "✓ Type-forwarding-generator built successfully" -ForegroundColor Green
}

# Define the assemblies we need to create shims for
$assemblies = @(
    @{
        ShimName = "Orleans.Core.Abstractions"
        GranvilleName = "Granville.Orleans.Core.Abstractions"
        TargetFramework = "net8.0"
    },
    @{
        ShimName = "Orleans.Serialization"
        GranvilleName = "Granville.Orleans.Serialization"
        TargetFramework = "net8.0"
    },
    @{
        ShimName = "Orleans.Serialization.Abstractions"
        GranvilleName = "Granville.Orleans.Serialization.Abstractions"
        TargetFramework = "netstandard2.0"
    },
    @{
        ShimName = "Orleans.CodeGenerator"
        GranvilleName = "Granville.Orleans.CodeGenerator"
        TargetFramework = "net8.0"
    },
    @{
        ShimName = "Orleans.Analyzers"
        GranvilleName = "Granville.Orleans.Analyzers"
        TargetFramework = "netstandard2.0"
    },
    @{
        ShimName = "Orleans.Reminders"
        GranvilleName = "Granville.Orleans.Reminders"
        TargetFramework = "net8.0"
    },
    @{
        ShimName = "Orleans.Core"
        GranvilleName = "Granville.Orleans.Core"
        TargetFramework = "net8.0"
    },
    @{
        ShimName = "Orleans.Serialization.SystemTextJson"
        GranvilleName = "Granville.Orleans.Serialization.SystemTextJson"
        TargetFramework = "net8.0"
    },
    @{
        ShimName = "Orleans.Runtime"
        GranvilleName = "Granville.Orleans.Runtime"
        TargetFramework = "net8.0"
    },
    @{
        ShimName = "Orleans.Sdk"
        GranvilleName = "Granville.Orleans.Sdk"
        TargetFramework = "net8.0"
    },
    @{
        ShimName = "Orleans.Server"
        GranvilleName = "Granville.Orleans.Server"
        TargetFramework = "net8.0"
    },
    @{
        ShimName = "Orleans.Client"
        GranvilleName = "Granville.Orleans.Client"
        TargetFramework = "net8.0"
    }
)

# Process each assembly
foreach ($asm in $assemblies) {
    $shimName = $asm.ShimName
    $granvilleName = $asm.GranvilleName
    $framework = $asm.TargetFramework
    
    $granvillePath = "../../src/$shimName/bin/Release/$framework/$granvilleName.dll"
    $shimPath = "shims-proper/$shimName.dll"
    
    if (!(Test-Path $granvillePath)) {
        Write-Host "Warning: $granvillePath not found, skipping..." -ForegroundColor Yellow
        continue
    }
    
    Write-Host "Generating shim for $shimName..." -ForegroundColor Green
    Write-Host "  Source: $granvillePath"
    Write-Host "  Target: $shimPath"
    
    # Run the generator
    & $dotnetCmd type-forwarding-generator/bin/Release/net9.0/GenerateTypeForwardingAssemblies.dll $granvillePath $shimPath
    
    if ($LASTEXITCODE -eq 0 -and (Test-Path $shimPath)) {
        $fileInfo = Get-Item $shimPath
        if ($fileInfo.Length -eq 0) {
            Write-Host "  Failed! Generated empty assembly: $shimPath" -ForegroundColor Red
        } else {
            Write-Host "  Success! Generated $shimPath" -ForegroundColor Green
        }
    } else {
        Write-Host "  Failed to generate $shimPath" -ForegroundColor Red
    }
}

Write-Host "`nDone! Generated shims:" -ForegroundColor Cyan
$hasErrors = $false
Get-ChildItem "shims-proper/*.dll" | ForEach-Object { 
    $size = "{0:N0}" -f $_.Length
    if ($_.Length -eq 0) {
        Write-Host "  - $($_.Name) ($size bytes)" -ForegroundColor Red
        $hasErrors = $true
    } else {
        Write-Host "  - $($_.Name) ($size bytes)"
    }
}

if ($hasErrors) {
    Write-Host "`n✗ Some shim assemblies failed to generate properly!" -ForegroundColor Red
    exit 1
} else {
    Write-Host "`n✓ Shim assemblies generated successfully!" -ForegroundColor Green
}