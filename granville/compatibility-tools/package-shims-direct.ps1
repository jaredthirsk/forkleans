#!/usr/bin/env pwsh
# Direct packaging script for Microsoft.Orleans.* shim assemblies

# Dynamically detect the current Granville Orleans version from built packages
$granvilleCorePackages = Get-ChildItem "../../Artifacts/Release/Granville.Orleans.Core.*.nupkg" | Where-Object { $_.Name -match "^Granville\.Orleans\.Core\.\d+\.\d+\.\d+(\.\d+)?\.nupkg$" }
if ($granvilleCorePackages) {
    $granvilleCorePackage = $granvilleCorePackages | Sort-Object Name | Select-Object -Last 1
    # Extract version from filename like "Granville.Orleans.Core.9.1.2.53.nupkg"
    if ($granvilleCorePackage.Name -match "Granville\.Orleans\.Core\.(\d+\.\d+\.\d+(?:\.\d+)?)\.nupkg") {
        $GranvilleVersion = $matches[1]
        Write-Host "Detected Granville Orleans version: $GranvilleVersion" -ForegroundColor Cyan
    } else {
        Write-Error "Could not parse version from package name: $($granvilleCorePackage.Name)"
        exit 1
    }
} else {
    Write-Error "Could not find Granville.Orleans.Core package to determine version"
    exit 1
}

$Version = "$GranvilleVersion-granville-shim"
$StableVersion = "$GranvilleVersion"

Write-Host "Creating Microsoft.Orleans.* shim NuGet packages..." -ForegroundColor Green
Write-Host "  Stable version: $StableVersion" -ForegroundColor Cyan  
Write-Host "  Prerelease version: $Version" -ForegroundColor Cyan

# Ensure output directory exists
New-Item -ItemType Directory -Force -Path "../../Artifacts/Release" | Out-Null

# Download nuget.exe if not present
if (!(Test-Path "nuget.exe")) {
    Write-Host "Downloading nuget.exe..." -ForegroundColor Yellow
    Invoke-WebRequest -Uri "https://dist.nuget.org/win-x86-commandline/latest/nuget.exe" -OutFile "nuget.exe"
}

# Create nuspec for each assembly
# Note: Dependencies should reference other Microsoft.Orleans.* shim packages, not Granville packages
$assemblies = @(
    @{PackageName="Microsoft.Orleans.Core"; DllName="Orleans.Core"; Deps=@("Microsoft.Orleans.Core.Abstractions", "Microsoft.Orleans.Serialization")},
    @{PackageName="Microsoft.Orleans.Core.Abstractions"; DllName="Orleans.Core.Abstractions"; Deps=@()},
    @{PackageName="Microsoft.Orleans.Runtime"; DllName="Orleans.Runtime"; Deps=@("Microsoft.Orleans.Core")},
    @{PackageName="Microsoft.Orleans.Server"; DllName="Orleans.Server"; Deps=@("Microsoft.Orleans.Runtime", "Microsoft.Orleans.Core")},
    @{PackageName="Microsoft.Orleans.Serialization"; DllName="Orleans.Serialization"; Deps=@("Microsoft.Orleans.Serialization.Abstractions")},
    @{PackageName="Microsoft.Orleans.Serialization.Abstractions"; DllName="Orleans.Serialization.Abstractions"; Deps=@()},
    @{PackageName="Microsoft.Orleans.Serialization.SystemTextJson"; DllName="Orleans.Serialization.SystemTextJson"; Deps=@("Microsoft.Orleans.Serialization")},
    @{PackageName="Microsoft.Orleans.Reminders"; DllName="Orleans.Reminders"; Deps=@("Microsoft.Orleans.Core")},
    @{PackageName="Microsoft.Orleans.Persistence.Memory"; DllName="Orleans.Persistence.Memory"; Deps=@("Microsoft.Orleans.Core")},
    @{PackageName="Microsoft.Orleans.Sdk"; DllName="Orleans.Sdk"; Deps=@("Microsoft.Orleans.Core", "Microsoft.Orleans.Analyzers", "Microsoft.Orleans.CodeGenerator")},
    @{PackageName="Microsoft.Orleans.Client"; DllName="Orleans.Client"; Deps=@("Microsoft.Orleans.Core")},
    @{PackageName="Microsoft.Orleans.CodeGenerator"; DllName="Orleans.CodeGenerator"; Deps=@()},
    @{PackageName="Microsoft.Orleans.Analyzers"; DllName="Orleans.Analyzers"; Deps=@()}
)

foreach ($assembly in $assemblies) {
    $packageName = $assembly.PackageName
    $dllName = $assembly.DllName
    $deps = $assembly.Deps
    
    if (-not (Test-Path "./shims-proper/$dllName.dll")) {
        Write-Host "  Skipping $packageName - DLL not found" -ForegroundColor Yellow
        continue
    }
    
    Write-Host "  Creating package for $packageName..." -ForegroundColor Cyan
    
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
    <title>$packageName Type Forwarding Assembly</title>
    <authors>Granville Orleans Fork</authors>
    <description>COMPATIBILITY SHIM: Type forwarding assembly that redirects $packageName types to Granville.Orleans. This package enables third-party Orleans packages to work with Granville.Orleans. Only use this if you're using Granville.Orleans and need compatibility with packages that depend on Microsoft.Orleans.</description>
    <projectUrl>https://github.com/jaredthirsk/orleans</projectUrl>
    <license type="expression">MIT</license>
    <tags>Orleans Granville Compatibility TypeForwarding Shim</tags>
$depXml
  </metadata>
  <files>
    <file src="shims-proper\$dllName.dll" target="lib\net8.0\$dllName.dll" />
  </files>
</package>
"@
    
    # Save nuspec
    $nuspec | Out-File -FilePath "$packageName.nuspec" -Encoding UTF8
    
    # Pack
    & .\nuget.exe pack "$packageName.nuspec" -OutputDirectory "../../Artifacts/Release" -NoDefaultExcludes
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "    Successfully created $packageName.$Version.nupkg" -ForegroundColor Green
        Remove-Item "$packageName.nuspec" -Force
    }
}

Write-Host "`nPackaging complete!" -ForegroundColor Green
Write-Host "Packages created in: ../../Artifacts/Release" -ForegroundColor Cyan