#!/usr/bin/env pwsh

# Script to create Microsoft.Orleans shim packages with proper dependencies on Granville.Orleans packages
# Version: 9.1.2.73-granville-shim

$ErrorActionPreference = "Stop"

$version = "9.1.2.76-granville-shim"
$granvilleVersion = "9.1.2.76"
$outputDir = "$PSScriptRoot/../../Artifacts/Release"
$shimDir = "$PSScriptRoot/shims-proper"
$tempDir = "$PSScriptRoot/temp-shim-packaging"

# Ensure output directory exists
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

# List of shims to package
$shimPackages = @(
    @{
        Name = "Microsoft.Orleans.Core.Abstractions"
        Dll = "Orleans.Core.Abstractions.dll"
        Dependencies = @(
            @{ Id = "Granville.Orleans.Core.Abstractions"; Version = $granvilleVersion }
        )
    },
    @{
        Name = "Microsoft.Orleans.Core"
        Dll = "Orleans.Core.dll"
        Dependencies = @(
            @{ Id = "Granville.Orleans.Core"; Version = $granvilleVersion },
            @{ Id = "Microsoft.Orleans.Core.Abstractions"; Version = $version }
        )
    },
    @{
        Name = "Microsoft.Orleans.Runtime"
        Dll = "Orleans.Runtime.dll"
        Dependencies = @(
            @{ Id = "Granville.Orleans.Runtime"; Version = $granvilleVersion },
            @{ Id = "Microsoft.Orleans.Core"; Version = $version }
        )
    },
    @{
        Name = "Microsoft.Orleans.Serialization.Abstractions"
        Dll = "Orleans.Serialization.Abstractions.dll"
        Dependencies = @(
            @{ Id = "Granville.Orleans.Serialization.Abstractions"; Version = $granvilleVersion }
        )
    },
    @{
        Name = "Microsoft.Orleans.Serialization"
        Dll = "Orleans.Serialization.dll"
        Dependencies = @(
            @{ Id = "Granville.Orleans.Serialization"; Version = $granvilleVersion },
            @{ Id = "Microsoft.Orleans.Serialization.Abstractions"; Version = $version }
        )
    }
)

foreach ($package in $shimPackages) {
    Write-Host "Creating package: $($package.Name) v$version" -ForegroundColor Cyan
    
    # Create temp directory for this package
    $pkgTempDir = "$tempDir/$($package.Name)"
    Remove-Item -Recurse -Force $pkgTempDir -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path "$pkgTempDir/lib/net8.0" | Out-Null
    
    # Copy the shim DLL
    $sourceDll = "$shimDir/$($package.Dll)"
    if (!(Test-Path $sourceDll)) {
        Write-Error "Shim DLL not found: $sourceDll"
        continue
    }
    
    Copy-Item $sourceDll "$pkgTempDir/lib/net8.0/"
    
    # Create the nuspec file
    $nuspecContent = @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>$($package.Name)</id>
    <version>$version</version>
    <authors>Granville Systems</authors>
    <owners>Granville Systems</owners>
    <requireLicenseAcceptance>false</requireLicenseAcceptance>
    <license type="expression">MIT</license>
    <projectUrl>https://github.com/granvillesystems/orleans</projectUrl>
    <description>Type-forwarding shim package for $($package.Name) that redirects to Granville.Orleans assemblies</description>
    <releaseNotes>Shim package for compatibility with Microsoft.Orleans</releaseNotes>
    <copyright>Copyright (c) Granville Systems</copyright>
    <tags>orleans actor-model cloud-computing microservice distributed-systems orleans-shim</tags>
    <dependencies>
      <group targetFramework="net8.0">
"@
    
    foreach ($dep in $package.Dependencies) {
        $nuspecContent += @"

        <dependency id="$($dep.Id)" version="$($dep.Version)" />
"@
    }
    
    $nuspecContent += @"

      </group>
    </dependencies>
  </metadata>
</package>
"@
    
    # Write the nuspec file
    $nuspecPath = "$pkgTempDir/$($package.Name).nuspec"
    $nuspecContent | Out-File -FilePath $nuspecPath -Encoding UTF8
    
    # Pack the package
    Write-Host "  Packing $($package.Name)..." -ForegroundColor Gray
    # Convert paths to Windows format for nuget.exe
    $winNuspecPath = $nuspecPath -replace '^/mnt/([a-z])/', '$1:/' -replace '/', '\'
    $winOutputDir = $outputDir -replace '^/mnt/([a-z])/', '$1:/' -replace '/', '\'
    & "$PSScriptRoot/nuget.exe" pack $winNuspecPath -OutputDirectory $winOutputDir -NoDefaultExcludes
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to pack $($package.Name)"
    } else {
        Write-Host "  ✓ Created $($package.Name).$version.nupkg" -ForegroundColor Green
    }
}

# Clean up temp directory
Remove-Item -Recurse -Force $tempDir -ErrorAction SilentlyContinue

Write-Host "`nAll shim packages created successfully in: $outputDir" -ForegroundColor Green