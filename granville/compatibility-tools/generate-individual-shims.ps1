#!/usr/bin/env pwsh

# Ensure we're in the right directory
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

# Create shims-proper directory if it doesn't exist
if (!(Test-Path "shims-proper")) {
    New-Item -ItemType Directory -Path "shims-proper" | Out-Null
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
    & .\type-forwarding-generator\GenerateTypeForwardingAssemblies.exe $granvillePath $shimPath
    
    if ($LASTEXITCODE -eq 0 -and (Test-Path $shimPath)) {
        Write-Host "  Success! Generated $shimPath" -ForegroundColor Green
    } else {
        Write-Host "  Failed to generate $shimPath" -ForegroundColor Red
    }
}

Write-Host "`nDone! Generated shims:" -ForegroundColor Cyan
Get-ChildItem "shims-proper/*.dll" | ForEach-Object { 
    $size = "{0:N0}" -f $_.Length
    Write-Host "  - $($_.Name) ($size bytes)"
}