#!/usr/bin/env pwsh
# Script to build Granville Orleans with code generation enabled

param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

Write-Host "Building Granville Orleans with Code Generation..." -ForegroundColor Green

# First, build a special version of Orleans.Core.Abstractions that includes generated types
$testProject = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <AssemblyName>Granville.Orleans.Core.Abstractions</AssemblyName>
    <RootNamespace>Orleans</RootNamespace>
    <OrleansBuildTimeCodeGen>true</OrleansBuildTimeCodeGen>
    <Orleans_DesignTimeBuild>false</Orleans_DesignTimeBuild>
  </PropertyGroup>
  
  <ItemGroup>
    <Compile Include="../../src/Orleans.Core.Abstractions/**/*.cs" />
    <Compile Remove="../../src/Orleans.Core.Abstractions/obj/**" />
    <Compile Remove="../../src/Orleans.Core.Abstractions/bin/**" />
  </ItemGroup>
  
  <ItemGroup>
    <PackageReference Include="Microsoft.Orleans.Sdk" Version="9.1.2" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Abstractions" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Logging" Version="8.0.1" />
  </ItemGroup>
  
  <ItemGroup>
    <ProjectReference Include="../../src/Orleans.Serialization/Orleans.Serialization.csproj" />
  </ItemGroup>
</Project>
"@

# Create temp directory
$tempDir = "temp-codegen-build"
if (Test-Path $tempDir) {
    Remove-Item -Path $tempDir -Recurse -Force
}
New-Item -ItemType Directory -Path $tempDir | Out-Null

# Write project file
$testProject | Out-File -FilePath "$tempDir/Orleans.Core.Abstractions.csproj" -Encoding UTF8

# Build it
Write-Host "Building Orleans.Core.Abstractions with code generation..." -ForegroundColor Cyan
Push-Location $tempDir
try {
    & dotnet-win build -c $Configuration -p:BuildAsGranville=true
    
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed"
    }
    
    # Check if codegen types were created
    $dllPath = "bin/$Configuration/net8.0/Granville.Orleans.Core.Abstractions.dll"
    if (Test-Path $dllPath) {
        Write-Host "Checking for OrleansCodeGen types..." -ForegroundColor Yellow
        $ildasmOutput = & dotnet-win ildasm "$(Get-Location)/$dllPath" 2>&1
        $codegenCount = ($ildasmOutput | Select-String "OrleansCodeGen").Count
        Write-Host "Found $codegenCount OrleansCodeGen references" -ForegroundColor Green
        
        if ($codegenCount -gt 0) {
            # Copy the DLL to the actual output location
            $targetPath = "../src/Orleans.Core.Abstractions/bin/Release/net8.0/Granville.Orleans.Core.Abstractions.dll"
            Copy-Item -Path $dllPath -Destination $targetPath -Force
            Write-Host "✓ Copied assembly with generated types to $targetPath" -ForegroundColor Green
        }
    }
} finally {
    Pop-Location
}

# Clean up
# Remove-Item -Path $tempDir -Recurse -Force

Write-Host "`n✓ Build complete!" -ForegroundColor Green