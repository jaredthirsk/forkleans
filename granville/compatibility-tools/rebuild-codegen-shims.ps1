#!/usr/bin/env pwsh

# Quick script to rebuild Microsoft.Orleans.CodeGenerator and Microsoft.Orleans.Sdk shim packages

$ErrorActionPreference = "Stop"

# Get version
$directoryBuildProps = Join-Path $PSScriptRoot "../../Directory.Build.props"
$xml = [xml](Get-Content $directoryBuildProps)
$versionPrefix = $xml.SelectSingleNode("//VersionPrefix").InnerText
$granvilleRevision = $xml.SelectSingleNode("//GranvilleRevision").InnerText
$version = "$versionPrefix.$granvilleRevision-granville-shim"
$granvilleVersion = "$versionPrefix.$granvilleRevision"

Write-Host "Building shim packages version: $version" -ForegroundColor Cyan

$outputDir = "$PSScriptRoot/../../Artifacts/Release"
$tempDir = "$PSScriptRoot/temp-rebuild"

# Clean temp dir
Remove-Item -Recurse -Force $tempDir -ErrorAction SilentlyContinue

# Package 1: Microsoft.Orleans.CodeGenerator
Write-Host "Creating Microsoft.Orleans.CodeGenerator shim..." -ForegroundColor Yellow
$pkgDir = "$tempDir/Microsoft.Orleans.CodeGenerator"
New-Item -ItemType Directory -Force -Path "$pkgDir/build" | Out-Null

# Create props file content directly
$propsContent = @'
<?xml version="1.0" encoding="utf-8"?>
<Project>
  <PropertyGroup>
    <!-- Set Orleans_DesignTimeBuild to true to prevent duplicate code generation -->
    <Orleans_DesignTimeBuild>true</Orleans_DesignTimeBuild>
  </PropertyGroup>
  
  <ItemGroup>
    <!-- Make Orleans_DesignTimeBuild visible to the compiler/analyzer -->
    <CompilerVisibleProperty Include="Orleans_DesignTimeBuild" />
  </ItemGroup>
</Project>
'@

$propsContent | Out-File -FilePath "$pkgDir/build/Microsoft.Orleans.CodeGenerator.props" -Encoding UTF8

# Create nuspec
$nuspecContent = @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>Microsoft.Orleans.CodeGenerator</id>
    <version>$version</version>
    <authors>Granville Systems</authors>
    <owners>Granville Systems</owners>
    <requireLicenseAcceptance>false</requireLicenseAcceptance>
    <license type="expression">MIT</license>
    <projectUrl>https://github.com/granvillesystems/orleans</projectUrl>
    <description>Type-forwarding shim package for Microsoft.Orleans.CodeGenerator that redirects to Granville.Orleans.CodeGenerator</description>
    <releaseNotes>Shim package for compatibility with Microsoft.Orleans</releaseNotes>
    <copyright>Copyright (c) Granville Systems</copyright>
    <tags>orleans actor-model cloud-computing microservice distributed-systems orleans-shim</tags>
    <dependencies>
      <group targetFramework="netstandard2.0">
        <dependency id="Granville.Orleans.CodeGenerator" version="$granvilleVersion" />
      </group>
    </dependencies>
  </metadata>
  <files>
    <file src="build\**" target="build\" />
  </files>
</package>
"@

$nuspecPath = "$pkgDir/Microsoft.Orleans.CodeGenerator.nuspec"
$nuspecContent | Out-File -FilePath $nuspecPath -Encoding UTF8

# Convert paths for Windows
$winNuspecPath = $nuspecPath -replace '^/mnt/([a-z])/', '$1:/' -replace '/', '\'
$winOutputDir = $outputDir -replace '^/mnt/([a-z])/', '$1:/' -replace '/', '\'

# Pack
& "$PSScriptRoot/nuget.exe" pack $winNuspecPath -OutputDirectory $winOutputDir -NoDefaultExcludes
Write-Host "Created Microsoft.Orleans.CodeGenerator.$version.nupkg" -ForegroundColor Green

# Package 2: Microsoft.Orleans.Sdk
Write-Host "Creating Microsoft.Orleans.Sdk shim..." -ForegroundColor Yellow
$pkgDir2 = "$tempDir/Microsoft.Orleans.Sdk"
New-Item -ItemType Directory -Force -Path "$pkgDir2/build" | Out-Null

# Create Sdk props content
$sdkPropsContent = @'
<?xml version="1.0" encoding="utf-8"?>
<Project>
  <PropertyGroup>
    <!-- Set Orleans_DesignTimeBuild to true to prevent duplicate code generation -->
    <Orleans_DesignTimeBuild>true</Orleans_DesignTimeBuild>
  </PropertyGroup>
  
  <ItemGroup>
    <!-- Make Orleans_DesignTimeBuild visible to the compiler/analyzer -->
    <CompilerVisibleProperty Include="Orleans_DesignTimeBuild" />
  </ItemGroup>
</Project>
'@

$sdkPropsContent | Out-File -FilePath "$pkgDir2/build/Microsoft.Orleans.Sdk.props" -Encoding UTF8

# Create SDK nuspec
$sdkNuspecContent = @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>Microsoft.Orleans.Sdk</id>
    <version>$version</version>
    <authors>Granville Systems</authors>
    <owners>Granville Systems</owners>
    <requireLicenseAcceptance>false</requireLicenseAcceptance>
    <license type="expression">MIT</license>
    <projectUrl>https://github.com/granvillesystems/orleans</projectUrl>
    <description>Type-forwarding shim package for Microsoft.Orleans.Sdk that redirects to Granville.Orleans.Sdk</description>
    <releaseNotes>Shim package for compatibility with Microsoft.Orleans</releaseNotes>
    <copyright>Copyright (c) Granville Systems</copyright>
    <tags>orleans actor-model cloud-computing microservice distributed-systems orleans-shim</tags>
    <dependencies>
      <group targetFramework="netstandard2.0">
        <dependency id="Granville.Orleans.Sdk" version="$granvilleVersion" />
      </group>
    </dependencies>
  </metadata>
  <files>
    <file src="build\**" target="build\" />
  </files>
</package>
"@

$sdkNuspecPath = "$pkgDir2/Microsoft.Orleans.Sdk.nuspec"
$sdkNuspecContent | Out-File -FilePath $sdkNuspecPath -Encoding UTF8

# Convert paths and pack
$winSdkNuspecPath = $sdkNuspecPath -replace '^/mnt/([a-z])/', '$1:/' -replace '/', '\'
& "$PSScriptRoot/nuget.exe" pack $winSdkNuspecPath -OutputDirectory $winOutputDir -NoDefaultExcludes
Write-Host "Created Microsoft.Orleans.Sdk.$version.nupkg" -ForegroundColor Green

# Cleanup
Remove-Item -Recurse -Force $tempDir -ErrorAction SilentlyContinue

Write-Host "`nShim packages created successfully!" -ForegroundColor Green
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Clear NuGet cache: dotnet nuget locals all --clear" -ForegroundColor Gray
Write-Host "2. Rebuild Shooter sample" -ForegroundColor Gray