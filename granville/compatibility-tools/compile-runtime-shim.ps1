#!/usr/bin/env pwsh

$ErrorActionPreference = "Stop"

Write-Host "Compiling Orleans.Runtime shim with internal types..." -ForegroundColor Cyan

# Create a temporary project
$tempDir = "temp-runtime-shim-compile"
if (Test-Path $tempDir) {
    Remove-Item -Path $tempDir -Recurse -Force
}
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

# Create project file
$projectContent = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <AssemblyName>Orleans.Runtime</AssemblyName>
    <RootNamespace>Orleans_Runtime</RootNamespace>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../shims-proper/Orleans.Runtime.cs" />
  </ItemGroup>
  <ItemGroup>
    <Reference Include="Granville.Orleans.Runtime">
      <HintPath>../../../src/Orleans.Runtime/bin/Release/net8.0/Granville.Orleans.Runtime.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Granville.Orleans.Core">
      <HintPath>../../../src/Orleans.Core/bin/Release/net8.0/Granville.Orleans.Core.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Granville.Orleans.Core.Abstractions">
      <HintPath>../../../src/Orleans.Core.Abstractions/bin/Release/net8.0/Granville.Orleans.Core.Abstractions.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
'@

$projectContent | Out-File -FilePath "$tempDir/Orleans.Runtime.csproj" -Encoding UTF8

# Build
Push-Location $tempDir
try {
    Write-Host "Restoring..."
    & dotnet-win restore 2>&1 | Out-Host
    
    Write-Host "Building..."
    & dotnet-win build -c Release 2>&1 | Out-Host
    
    if ($LASTEXITCODE -eq 0) {
        # Copy the output
        Copy-Item -Path "bin/Release/net8.0/Orleans.Runtime.dll" -Destination "../shims-proper/Orleans.Runtime.dll" -Force
        Write-Host "✓ Successfully built Orleans.Runtime shim with internal types!" -ForegroundColor Green
    } else {
        Write-Host "✗ Build failed" -ForegroundColor Red
    }
} finally {
    Pop-Location
}

# Cleanup
Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue