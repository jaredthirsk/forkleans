#!/usr/bin/env pwsh
# Manually package CodeGenerator and Analyzers which have issues with Directory.Build.targets.pack

param(
    [string]$Configuration = "Release"
)

Write-Host "Manually packaging CodeGenerator and Analyzers..." -ForegroundColor Green

# Build first
Write-Host "Building projects..." -ForegroundColor Cyan
dotnet build src/Orleans.CodeGenerator/Orleans.CodeGenerator.csproj -c $Configuration
dotnet build src/Orleans.Analyzers/Orleans.Analyzers.csproj -c $Configuration

# Create temp directory for packaging
$tempDir = New-Item -ItemType Directory -Force -Path "temp-pack"

# Package Granville.Orleans.CodeGenerator
Write-Host "`nPackaging Granville.Orleans.CodeGenerator..." -ForegroundColor Cyan
$codegenDir = "$tempDir/codegen"
New-Item -ItemType Directory -Force -Path "$codegenDir/lib/netstandard2.0" | Out-Null
New-Item -ItemType Directory -Force -Path "$codegenDir/analyzers/dotnet/cs" | Out-Null

# Copy files
Copy-Item "src/Orleans.CodeGenerator/bin/$Configuration/netstandard2.0/Granville.Orleans.CodeGenerator.dll" "$codegenDir/analyzers/dotnet/cs/"
Copy-Item "src/Orleans.CodeGenerator/bin/$Configuration/netstandard2.0/Granville.Orleans.CodeGenerator.pdb" "$codegenDir/analyzers/dotnet/cs/" -ErrorAction SilentlyContinue

# Create nuspec
$codegenNuspec = @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>Granville.Orleans.CodeGenerator</id>
    <version>9.1.2.51</version>
    <authors>Granville RPC Contributors</authors>
    <description>Code generator for Granville Orleans</description>
    <dependencies>
      <group targetFramework=".NETStandard2.0">
        <dependency id="Microsoft.CodeAnalysis.CSharp" version="4.11.0" />
      </group>
    </dependencies>
  </metadata>
</package>
"@

$codegenNuspec | Out-File "$codegenDir/Granville.Orleans.CodeGenerator.nuspec" -Encoding UTF8

# Package it
Push-Location $codegenDir
& nuget pack Granville.Orleans.CodeGenerator.nuspec -OutputDirectory "../../Artifacts/Release"
Pop-Location

# Package Granville.Orleans.Analyzers
Write-Host "`nPackaging Granville.Orleans.Analyzers..." -ForegroundColor Cyan
$analyzersDir = "$tempDir/analyzers"
New-Item -ItemType Directory -Force -Path "$analyzersDir/analyzers/dotnet/cs" | Out-Null

# Copy files
Copy-Item "src/Orleans.Analyzers/bin/$Configuration/netstandard2.0/Granville.Orleans.Analyzers.dll" "$analyzersDir/analyzers/dotnet/cs/"
Copy-Item "src/Orleans.Analyzers/bin/$Configuration/netstandard2.0/Granville.Orleans.Analyzers.pdb" "$analyzersDir/analyzers/dotnet/cs/" -ErrorAction SilentlyContinue

# Create nuspec
$analyzersNuspec = @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>Granville.Orleans.Analyzers</id>
    <version>9.1.2.51</version>
    <authors>Granville RPC Contributors</authors>
    <description>Analyzers for Granville Orleans</description>
    <dependencies>
      <group targetFramework=".NETStandard2.0">
        <dependency id="Microsoft.CodeAnalysis.CSharp" version="4.11.0" />
      </group>
    </dependencies>
  </metadata>
</package>
"@

$analyzersNuspec | Out-File "$analyzersDir/Granville.Orleans.Analyzers.nuspec" -Encoding UTF8

# Package it
Push-Location $analyzersDir
& nuget pack Granville.Orleans.Analyzers.nuspec -OutputDirectory "../../Artifacts/Release"
Pop-Location

# Clean up
Remove-Item -Recurse -Force $tempDir

Write-Host "`nPackaging complete!" -ForegroundColor Green
Write-Host "Packages created in Artifacts/Release/" -ForegroundColor Cyan