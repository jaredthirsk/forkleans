#!/usr/bin/env pwsh

$ErrorActionPreference = "Stop"

Write-Host "Compiling Orleans.Serialization shim..." -ForegroundColor Cyan

# Create a temporary project
$tempDir = "temp-serialization-shim-compile"
if (Test-Path $tempDir) {
    Remove-Item -Path $tempDir -Recurse -Force
}
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

# Create project file
$projectContent = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <AssemblyName>Orleans.Serialization</AssemblyName>
    <RootNamespace>Orleans_Serialization</RootNamespace>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../shims-proper/Orleans.Serialization.cs" />
  </ItemGroup>
  <ItemGroup>
    <Reference Include="Granville.Orleans.Serialization">
      <HintPath>../../../src/Orleans.Serialization/bin/Release/net8.0/Granville.Orleans.Serialization.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Granville.Orleans.Serialization.Abstractions">
      <HintPath>../../../src/Orleans.Serialization.Abstractions/bin/Release/netstandard2.0/Granville.Orleans.Serialization.Abstractions.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
'@

$projectContent | Out-File -FilePath "$tempDir/Orleans.Serialization.csproj" -Encoding UTF8

# Build
Push-Location $tempDir
try {
    Write-Host "Restoring..."
    & dotnet-win restore 2>&1 | Out-Host
    
    Write-Host "Building..."
    & dotnet-win build -c Release 2>&1 | Out-Host
    
    if ($LASTEXITCODE -eq 0) {
        # Copy the output
        Copy-Item -Path "bin/Release/net8.0/Orleans.Serialization.dll" -Destination "../shims-proper/Orleans.Serialization.dll" -Force
        Write-Host "✓ Successfully built Orleans.Serialization shim!" -ForegroundColor Green
    } else {
        Write-Host "✗ Build failed" -ForegroundColor Red
    }
} finally {
    Pop-Location
}

# Cleanup
Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue