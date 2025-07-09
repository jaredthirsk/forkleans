#!/usr/bin/env pwsh
# Test if Orleans source generator works

$ErrorActionPreference = "Stop"

# Create temp directory
$tempDir = "temp-codegen-test"
if (Test-Path $tempDir) {
    Remove-Item -Path $tempDir -Recurse -Force
}
New-Item -ItemType Directory -Path $tempDir | Out-Null

# Create test project
$testProject = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <OrleansBuildTimeCodeGen>true</OrleansBuildTimeCodeGen>
    <Granville_DesignTimeBuild>false</Granville_DesignTimeBuild>
    <Orleans_DesignTimeBuild>false</Orleans_DesignTimeBuild>
  </PropertyGroup>
  
  <ItemGroup>
    <ProjectReference Include="../../../src/Orleans.CodeGenerator/Orleans.CodeGenerator.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <ProjectReference Include="../../../src/Orleans.Analyzers/Orleans.Analyzers.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <ProjectReference Include="../../../src/Orleans.Core.Abstractions/Orleans.Core.Abstractions.csproj" />
  </ItemGroup>
</Project>
'@

$testCode = @'
using Orleans;
using Orleans.Runtime;

namespace TestCodeGen
{
    public class TestClass
    {
        public void TestMethod()
        {
            var grainId = GrainId.Create("test", "key");
            var siloAddress = SiloAddress.New("localhost", 11111);
        }
    }
}
'@

$testProject | Out-File -FilePath "$tempDir/TestCodeGen.csproj" -Encoding UTF8
$testCode | Out-File -FilePath "$tempDir/TestClass.cs" -Encoding UTF8

# Build the project
Push-Location $tempDir
try {
    Write-Host "Building test project..." -ForegroundColor Yellow
    & dotnet-win restore
    & dotnet-win build -c Release -v d
    
    # Check for generated types
    $outputDll = "bin/Release/net8.0/TestCodeGen.dll"
    if (Test-Path $outputDll) {
        Write-Host "Checking for generated types..." -ForegroundColor Yellow
        $ildasmOutput = & dotnet-win ildasm "$(Get-Location)/$outputDll" 2>&1 | Out-String
        $orleansCodeGenTypes = $ildasmOutput | Select-String "OrleansCodeGen" | ForEach-Object { $_.Line }
        
        Write-Host "Found OrleansCodeGen types:" -ForegroundColor Green
        $orleansCodeGenTypes | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
        
        if ($orleansCodeGenTypes.Count -gt 0) {
            Write-Host "✓ Code generation is working!" -ForegroundColor Green
        } else {
            Write-Host "✗ No OrleansCodeGen types found" -ForegroundColor Red
        }
    } else {
        Write-Host "✗ Build failed - no output DLL found" -ForegroundColor Red
    }
} finally {
    Pop-Location
}

Write-Host "Test complete!" -ForegroundColor Green