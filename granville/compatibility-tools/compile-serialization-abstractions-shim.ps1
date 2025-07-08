#!/usr/bin/env pwsh

$ErrorActionPreference = "Stop"

Write-Host "Compiling Orleans.Serialization.Abstractions shim..." -ForegroundColor Cyan

# Create a temporary project
$tempDir = "temp-serialization-abstractions-shim-compile"
if (Test-Path $tempDir) {
    Remove-Item -Path $tempDir -Recurse -Force
}
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

# Create project file
$projectContent = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <AssemblyName>Orleans.Serialization.Abstractions</AssemblyName>
    <RootNamespace>Orleans_Serialization_Abstractions</RootNamespace>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../shims-proper/Orleans.Serialization.Abstractions.cs" />
  </ItemGroup>
  <ItemGroup>
    <Reference Include="Granville.Orleans.Serialization.Abstractions">
      <HintPath>../../../src/Orleans.Serialization.Abstractions/bin/Release/netstandard2.0/Granville.Orleans.Serialization.Abstractions.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
'@

$projectContent | Out-File -FilePath "$tempDir/Orleans.Serialization.Abstractions.csproj" -Encoding UTF8

# Build
Push-Location $tempDir
try {
    Write-Host "Restoring..."
    & dotnet-win restore 2>&1 | Out-Host
    
    Write-Host "Building..."
    & dotnet-win build -c Release 2>&1 | Out-Host
    
    if ($LASTEXITCODE -eq 0) {
        # Copy the output
        Copy-Item -Path "bin/Release/netstandard2.0/Orleans.Serialization.Abstractions.dll" -Destination "../shims-proper/Orleans.Serialization.Abstractions.dll" -Force
        Write-Host "✓ Successfully built Orleans.Serialization.Abstractions shim!" -ForegroundColor Green
    } else {
        Write-Host "✗ Build failed" -ForegroundColor Red
    }
} finally {
    Pop-Location
}

# Cleanup
Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue