#!/usr/bin/env pwsh
# Script to inject OrleansCodeGen types into Granville assemblies

param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Injecting OrleansCodeGen Types into Granville Assemblies ===" -ForegroundColor Cyan

# Step 1: Create a generator project
$generatorDir = "temp-codegen-generator"
if (Test-Path $generatorDir) {
    Remove-Item -Path $generatorDir -Recurse -Force
}
New-Item -ItemType Directory -Path $generatorDir | Out-Null

# Create a project that references all types we need to generate codecs for
$generatorProject = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <OrleansBuildTimeCodeGen>true</OrleansBuildTimeCodeGen>
    <Orleans_DesignTimeBuild>false</Orleans_DesignTimeBuild>
  </PropertyGroup>
  
  <ItemGroup>
    <PackageReference Include="Microsoft.Orleans.Sdk" Version="9.1.2" />
  </ItemGroup>
  
  <ItemGroup>
    <ProjectReference Include="../../src/Orleans.Core.Abstractions/Orleans.Core.Abstractions.csproj" />
    <ProjectReference Include="../../src/Orleans.Core/Orleans.Core.csproj" />
    <ProjectReference Include="../../src/Orleans.Serialization/Orleans.Serialization.csproj" />
  </ItemGroup>
</Project>
'@

$generatorCode = @'
using Orleans;
using Orleans.Runtime;

// Reference types to trigger code generation
public static class TypeReferences
{
    public static void Reference()
    {
        _ = typeof(GrainId);
        _ = typeof(SiloAddress);
        _ = typeof(ActivationId);
        _ = typeof(MembershipVersion);
        _ = typeof(GrainType);
        _ = typeof(GrainInterfaceType);
        _ = typeof(Orleans.Metadata.ClusterManifest);
        _ = typeof(Orleans.Metadata.GrainManifest);
    }
}
'@

# Write files
$generatorProject | Out-File -FilePath "$generatorDir/CodeGenerator.csproj" -Encoding UTF8
$generatorCode | Out-File -FilePath "$generatorDir/Program.cs" -Encoding UTF8

# Build the generator project
Write-Host "Building code generator project..." -ForegroundColor Yellow
Push-Location $generatorDir
try {
    # First restore
    & dotnet-win restore
    if ($LASTEXITCODE -ne 0) {
        throw "Restore failed"
    }
    
    # Then build
    & dotnet-win build -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed"
    }
    
    # Check the output
    $outputDll = "bin/$Configuration/net8.0/CodeGenerator.dll"
    if (Test-Path $outputDll) {
        Write-Host "Checking for generated types..." -ForegroundColor Yellow
        $ildasmOutput = & dotnet-win ildasm "$(Get-Location)/$outputDll" 2>&1 | Out-String
        $orleansCodeGenTypes = $ildasmOutput | Select-String "class.*OrleansCodeGen" | ForEach-Object { $_.Line }
        
        Write-Host "Found $($orleansCodeGenTypes.Count) OrleansCodeGen types:" -ForegroundColor Green
        $orleansCodeGenTypes | Select-Object -First 10 | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
        
        if ($orleansCodeGenTypes.Count -gt 0) {
            Write-Host "`n✓ Successfully generated OrleansCodeGen types!" -ForegroundColor Green
            Write-Host "`nNext steps:" -ForegroundColor Cyan
            Write-Host "1. Use ILRepack or similar tool to merge these types into Granville assemblies"
            Write-Host "2. Or modify the Granville build process to include code generation"
        }
    }
} finally {
    Pop-Location
}

Write-Host "`n✓ Analysis complete!" -ForegroundColor Green