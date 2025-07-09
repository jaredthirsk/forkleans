#!/usr/bin/env pwsh
# Generate OrleansCodeGen types for Granville Orleans assemblies

$ErrorActionPreference = "Stop"

Write-Host "=== Generating OrleansCodeGen Types for Granville Orleans ===" -ForegroundColor Cyan

# Step 1: Create a temporary generator project
$tempDir = "temp-granville-codegen"
if (Test-Path $tempDir) {
    Remove-Item -Path $tempDir -Recurse -Force
}
New-Item -ItemType Directory -Path $tempDir | Out-Null

# Create a project that references Granville Orleans assemblies and generates types
$generatorProject = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <OrleansBuildTimeCodeGen>true</OrleansBuildTimeCodeGen>
    <Granville_DesignTimeBuild>false</Granville_DesignTimeBuild>
    <DesignTimeBuild>false</DesignTimeBuild>
  </PropertyGroup>
  
  <ItemGroup>
    <!-- Reference built Granville assemblies -->
    <Reference Include="Granville.Orleans.Core.Abstractions">
      <HintPath>../../src/Orleans.Core.Abstractions/bin/Release/net8.0/Granville.Orleans.Core.Abstractions.dll</HintPath>
    </Reference>
    <Reference Include="Granville.Orleans.Core">
      <HintPath>../../src/Orleans.Core/bin/Release/net8.0/Granville.Orleans.Core.dll</HintPath>
    </Reference>
    <Reference Include="Granville.Orleans.Serialization">
      <HintPath>../../src/Orleans.Serialization/bin/Release/net8.0/Granville.Orleans.Serialization.dll</HintPath>
    </Reference>
    <Reference Include="Granville.Orleans.Serialization.Abstractions">
      <HintPath>../../src/Orleans.Serialization.Abstractions/bin/Release/net8.0/Granville.Orleans.Serialization.Abstractions.dll</HintPath>
    </Reference>
    <Reference Include="Granville.Orleans.Runtime">
      <HintPath>../../src/Orleans.Runtime/bin/Release/net8.0/Granville.Orleans.Runtime.dll</HintPath>
    </Reference>
  </ItemGroup>
  
  <ItemGroup>
    <!-- Include the Orleans source generator -->
    <PackageReference Include="Microsoft.Orleans.Sdk" Version="9.1.2" />
  </ItemGroup>
</Project>
'@

$generatorCode = @'
using Orleans;
using Orleans.Runtime;

namespace GranvilleCodeGenGenerator
{
    public class TypeReferences
    {
        public void ReferenceTypes()
        {
            // Reference key Orleans types to trigger code generation
            _ = typeof(GrainId);
            _ = typeof(SiloAddress);
            _ = typeof(ActivationId);
            _ = typeof(MembershipVersion);
            _ = typeof(GrainType);
            _ = typeof(GrainInterfaceType);
            _ = typeof(Orleans.Metadata.ClusterManifest);
            _ = typeof(Orleans.Metadata.GrainManifest);
            _ = typeof(Orleans.Runtime.ReminderEntry);
        }
    }
}
'@

# Write the project files
$generatorProject | Out-File -FilePath "$tempDir/GranvilleCodeGenGenerator.csproj" -Encoding UTF8
$generatorCode | Out-File -FilePath "$tempDir/TypeReferences.cs" -Encoding UTF8

# Step 2: Build the generator project
Write-Host "Building generator project..." -ForegroundColor Yellow
Push-Location $tempDir
try {
    & dotnet-win restore
    if ($LASTEXITCODE -ne 0) {
        throw "Restore failed"
    }
    
    & dotnet-win build -c Release -v n
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed"
    }
    
    # Step 3: Check for generated types
    $outputDll = "bin/Release/net8.0/GranvilleCodeGenGenerator.dll"
    if (Test-Path $outputDll) {
        Write-Host "Checking for generated types..." -ForegroundColor Yellow
        $fullPath = (Get-Item $outputDll).FullName
        $ildasmOutput = & dotnet-win ildasm "$fullPath" 2>&1 | Out-String
        $orleansCodeGenTypes = $ildasmOutput | Select-String "OrleansCodeGen" | ForEach-Object { $_.Line }
        
        Write-Host "Found $($orleansCodeGenTypes.Count) OrleansCodeGen types:" -ForegroundColor Green
        $orleansCodeGenTypes | Select-Object -First 20 | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
        
        if ($orleansCodeGenTypes.Count -gt 0) {
            Write-Host "✓ Successfully generated OrleansCodeGen types!" -ForegroundColor Green
            
            # Extract and save the types
            $orleasCodeGenOutput = "orleanscodegen-types-granville.txt"
            $orleansCodeGenTypes | Out-File -FilePath "../$orleasCodeGenOutput" -Encoding UTF8
            Write-Host "Saved types to $orleasCodeGenOutput" -ForegroundColor Green
        } else {
            Write-Host "✗ No OrleansCodeGen types found" -ForegroundColor Red
        }
    } else {
        Write-Host "✗ Build failed - no output DLL found" -ForegroundColor Red
    }
} finally {
    Pop-Location
}

Write-Host "✓ Generation complete!" -ForegroundColor Green