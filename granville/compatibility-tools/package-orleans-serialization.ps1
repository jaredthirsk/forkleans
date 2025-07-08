#!/usr/bin/env pwsh

$ErrorActionPreference = "Stop"

Write-Host "Creating Orleans.Serialization NuGet package..." -ForegroundColor Cyan

$version = "9.1.2.60-granville-shim"
$packageId = "Microsoft.Orleans.Serialization"

# Create package directory structure
$packageDir = "package-orleans-serialization"
if (Test-Path $packageDir) {
    Remove-Item -Path $packageDir -Recurse -Force
}

$libDir = "$packageDir/lib/net8.0"
New-Item -ItemType Directory -Path $libDir -Force | Out-Null

# Copy the shim DLL
$shimPath = "shims-proper/Orleans.Serialization.dll"
if (-not (Test-Path $shimPath)) {
    Write-Host "✗ Shim not found at $shimPath" -ForegroundColor Red
    exit 1
}

Copy-Item -Path $shimPath -Destination "$libDir/Orleans.Serialization.dll" -Force
Write-Host "✓ Copied shim DLL (size: $((Get-Item $shimPath).Length) bytes)" -ForegroundColor Green

# Create .nuspec file
$nuspecContent = @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
  <metadata>
    <id>$packageId</id>
    <version>$version</version>
    <title>Orleans.Serialization</title>
    <authors>Granville</authors>
    <owners>Granville</owners>
    <description>Orleans Serialization shim for Granville Orleans compatibility - includes TypeManifestProviderBase and all type forwards</description>
    <projectUrl>https://github.com/dotnet/orleans</projectUrl>
    <requireLicenseAcceptance>false</requireLicenseAcceptance>
    <tags>Orleans Cloud-Computing Actor-Model Actors Distributed-Systems C# .NET</tags>
  </metadata>
</package>
"@

$nuspecPath = "$packageDir/$packageId.nuspec"
$nuspecContent | Out-File -FilePath $nuspecPath -Encoding UTF8

# Pack using NuGet CLI directly (avoiding dotnet pack issues)
Write-Host "Packing with NuGet..." -ForegroundColor Yellow

# First, let's try using dotnet pack with minimal project
$projectContent = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <NoBuild>true</NoBuild>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <EnableDefaultItems>false</EnableDefaultItems>
    <SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>
    <NoWarn>NU5128</NoWarn>
  </PropertyGroup>
</Project>
"@

$projectPath = "$packageDir/Orleans.Serialization.csproj"
$projectContent | Out-File -FilePath $projectPath -Encoding UTF8

# Create local nuget feed if it doesn't exist
$localFeed = "../../Artifacts/Release"
if (-not (Test-Path $localFeed)) {
    New-Item -ItemType Directory -Path $localFeed -Force | Out-Null
}

# Use a simple zip approach to create the nupkg
Push-Location $packageDir
try {
    # Create _rels directory
    New-Item -ItemType Directory -Path "_rels" -Force | Out-Null
    $relsContent = @'
<?xml version="1.0" encoding="utf-8"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Type="http://schemas.microsoft.com/packaging/2010/07/manifest" Target="/Microsoft.Orleans.Serialization.nuspec" Id="Rc7a5f2b7e6944e0f" />
</Relationships>
'@
    $relsContent | Out-File -FilePath "_rels/.rels" -Encoding UTF8

    # Create [Content_Types].xml
    $contentTypesContent = @'
<?xml version="1.0" encoding="utf-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
  <Default Extension="nuspec" ContentType="application/octet-stream" />
  <Default Extension="dll" ContentType="application/octet-stream" />
  <Default Extension="psmdcp" ContentType="application/vnd.openxmlformats-package.core-properties+xml" />
</Types>
'@
    [System.IO.File]::WriteAllText("$PWD/[Content_Types].xml", $contentTypesContent)

    # Create package using zip
    $nupkgPath = "$localFeed/$packageId.$version.nupkg"
    if (Test-Path $nupkgPath) {
        Remove-Item -Path $nupkgPath -Force
    }

    # Use PowerShell's Compress-Archive
    Compress-Archive -Path * -DestinationPath "$nupkgPath.zip" -Force
    Move-Item -Path "$nupkgPath.zip" -Destination $nupkgPath -Force

    Write-Host "✓ Package created at: $nupkgPath" -ForegroundColor Green
    
} finally {
    Pop-Location
}

# Cleanup
Remove-Item -Path $packageDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "✓ Successfully created Orleans.Serialization package v$version" -ForegroundColor Green
Write-Host "Package location: $localFeed/$packageId.$version.nupkg" -ForegroundColor Cyan
