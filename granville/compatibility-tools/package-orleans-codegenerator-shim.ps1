#!/usr/bin/env pwsh

# Script to package Microsoft.Orleans.CodeGenerator shim with proper property forwarding

$ErrorActionPreference = "Stop"

# Read version from Directory.Build.props
$directoryBuildProps = Join-Path $PSScriptRoot "../../Directory.Build.props"
if (Test-Path $directoryBuildProps) {
    $xml = [xml](Get-Content $directoryBuildProps)
    $versionPrefix = $xml.SelectSingleNode("//VersionPrefix").InnerText
    $granvilleRevision = $xml.SelectSingleNode("//GranvilleRevision").InnerText
    $version = "$versionPrefix.$granvilleRevision-granville-shim"
    Write-Host "Using version: $version" -ForegroundColor Yellow
} else {
    Write-Error "Directory.Build.props not found"
    exit 1
}

$outputDir = "$PSScriptRoot/../../Artifacts/Release"
$tempDir = "$PSScriptRoot/temp-codegen-shim"

# Clean temp directory
Remove-Item -Recurse -Force $tempDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null

# Create the package structure
$buildDir = "$tempDir/build"
New-Item -ItemType Directory -Force -Path $buildDir | Out-Null

# Copy the props file
Copy-Item "$PSScriptRoot/shims-proper/Microsoft.Orleans.CodeGenerator.props" "$buildDir/Microsoft.Orleans.CodeGenerator.props"

# Create nuspec file
$nuspecContent = @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>Microsoft.Orleans.CodeGenerator</id>
    <version>$version</version>
    <authors>Microsoft,Granville</authors>
    <owners>Microsoft,Granville</owners>
    <requireLicenseAcceptance>false</requireLicenseAcceptance>
    <license type="expression">MIT</license>
    <description>Granville shim package for Microsoft.Orleans.CodeGenerator - forwards to Granville.Orleans.CodeGenerator</description>
    <tags>Orleans Cloud-Computing Actor-Model Actors Distributed-Systems C# .NET</tags>
    <dependencies>
      <dependency id="Granville.Orleans.CodeGenerator" version="$versionPrefix.$granvilleRevision" />
    </dependencies>
  </metadata>
  <files>
    <file src="build\**" target="build\" />
  </files>
</package>
"@

$nuspecPath = "$tempDir/Microsoft.Orleans.CodeGenerator.nuspec"
$nuspecContent | Out-File -FilePath $nuspecPath -Encoding UTF8

# Convert paths for Windows
$winNuspecPath = $nuspecPath -replace '^/mnt/([a-z])/', '$1:/' -replace '/', '\'
$winOutputDir = $outputDir -replace '^/mnt/([a-z])/', '$1:/' -replace '/', '\'

# Pack the package
Write-Host "Packing Microsoft.Orleans.CodeGenerator shim..." -ForegroundColor Cyan
& "$PSScriptRoot/nuget.exe" pack $winNuspecPath -OutputDirectory $winOutputDir -NoDefaultExcludes

if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ Created Microsoft.Orleans.CodeGenerator.$version.nupkg" -ForegroundColor Green
} else {
    Write-Error "Failed to pack Microsoft.Orleans.CodeGenerator shim"
}

# Clean up
Remove-Item -Recurse -Force $tempDir -ErrorAction SilentlyContinue