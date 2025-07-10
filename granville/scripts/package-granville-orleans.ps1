#!/usr/bin/env pwsh
# Package already built Granville.Orleans assemblies

param(
    [string]$Version
)

# Read version from Directory.Build.props if not provided
if (!$Version) {
    $directoryBuildProps = Join-Path $PSScriptRoot "../../Directory.Build.props"
    if (Test-Path $directoryBuildProps) {
        $xml = [xml](Get-Content $directoryBuildProps)
        $versionPrefix = $xml.SelectSingleNode("//VersionPrefix").InnerText
        $granvilleRevision = $xml.SelectSingleNode("//GranvilleRevision").InnerText
        $Version = "$versionPrefix.$granvilleRevision"
        Write-Host "Using version from Directory.Build.props: $Version" -ForegroundColor Yellow
    } else {
        Write-Error "Version parameter is required or Directory.Build.props must exist with VersionPrefix and GranvilleRevision"
        exit 1
    }
}

Write-Host "Creating Granville.Orleans NuGet packages..." -ForegroundColor Green

# Ensure output directory exists
New-Item -ItemType Directory -Force -Path "Artifacts/Release" | Out-Null

# Create packages for core assemblies
$assemblies = @(
    @{Name="Granville.Orleans.Core.Abstractions"; Deps=@("Granville.Orleans.Serialization.Abstractions")},
    @{Name="Granville.Orleans.Core"; Deps=@("Granville.Orleans.Core.Abstractions", "Granville.Orleans.Serialization")},
    @{Name="Granville.Orleans.Serialization.Abstractions"; Deps=@()},
    @{Name="Granville.Orleans.Serialization"; Deps=@("Granville.Orleans.Serialization.Abstractions")},
    @{Name="Granville.Orleans.CodeGenerator"; Deps=@()},
    @{Name="Granville.Orleans.Analyzers"; Deps=@()},
    @{Name="Granville.Orleans.Runtime"; Deps=@("Granville.Orleans.Core")},
    @{Name="Granville.Orleans.Sdk"; Deps=@("Granville.Orleans.Core", "Granville.Orleans.CodeGenerator", "Granville.Orleans.Analyzers")},
    @{Name="Granville.Orleans.Server"; Deps=@("Granville.Orleans.Runtime", "Granville.Orleans.Core")}
)

foreach ($assembly in $assemblies) {
    $packageName = $assembly.Name
    $deps = $assembly.Deps
    
    Write-Host "  Creating package for $packageName..." -ForegroundColor Cyan
    
    # Find the built dll
    $dllPath = Get-ChildItem -Path "src" -Recurse -Filter "$packageName.dll" | Where-Object { $_.Directory.Name -eq "net8.0" -or $_.Directory.Name -eq "netstandard2.0" } | Select-Object -First 1
    
    if (-not $dllPath) {
        Write-Warning "  Skipping $packageName - DLL not found"
        continue
    }
    
    # Create a temporary directory for packaging
    $tempDir = New-Item -ItemType Directory -Path "temp-pack/$packageName" -Force
    $libDir = New-Item -ItemType Directory -Path "$tempDir/lib/net8.0" -Force
    
    # Copy the dll
    Copy-Item $dllPath.FullName -Destination $libDir
    
    # Build dependencies XML
    $depXml = ""
    if ($deps.Count -gt 0) {
        $depItems = $deps | ForEach-Object { "        <dependency id=`"$_`" version=`"$Version`" />" }
        $depXml = @"
    <dependencies>
      <group targetFramework="net8.0">
$($depItems -join "`n")
      </group>
    </dependencies>
"@
    }
    
    # Create nuspec
    $nuspec = @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd">
  <metadata>
    <id>$packageName</id>
    <version>$Version</version>
    <title>$packageName</title>
    <authors>Granville Orleans Fork</authors>
    <description>Granville Orleans - A fork of Microsoft Orleans with renamed assemblies to avoid NuGet namespace conflicts. This is the $packageName package.</description>
    <projectUrl>https://github.com/jaredthirsk/orleans</projectUrl>
    <license type="expression">MIT</license>
    <tags>Orleans Granville Actor</tags>
$depXml
  </metadata>
</package>
"@
    
    # Save nuspec
    $nuspec | Out-File -FilePath "$tempDir/$packageName.nuspec" -Encoding UTF8
    
    # Download nuget.exe if not present
    if (!(Test-Path "granville/compatibility-tools/nuget.exe")) {
        Write-Host "Using existing nuget.exe..."
    }
    
    # Pack using nuget.exe
    & granville/compatibility-tools/nuget.exe pack "$tempDir/$packageName.nuspec" -OutputDirectory "Artifacts/Release" -NoDefaultExcludes
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "    Successfully created $packageName.$Version.nupkg" -ForegroundColor Green
    }
}

# Cleanup
Remove-Item -Path "temp-pack" -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "`nPackaging complete!" -ForegroundColor Green
Write-Host "Packages created in: Artifacts/Release" -ForegroundColor Cyan