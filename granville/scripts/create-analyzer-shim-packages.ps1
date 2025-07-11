#!/usr/bin/env pwsh
# Create Microsoft.Orleans analyzer shim packages that forward to Granville versions

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

Write-Host "Creating Microsoft.Orleans analyzer shim packages..." -ForegroundColor Green

$shimPackages = @(
    @{
        PackageId = "Microsoft.Orleans.Analyzers"
        TargetPackage = "Granville.Orleans.Analyzers"
        Description = "Shim package that forwards to Granville.Orleans.Analyzers"
    },
    @{
        PackageId = "Microsoft.Orleans.CodeGenerator"
        TargetPackage = "Granville.Orleans.CodeGenerator"
        Description = "Shim package that forwards to Granville.Orleans.CodeGenerator"
    }
)

$tempDir = Join-Path $PSScriptRoot "temp-analyzer-shims"
if (Test-Path $tempDir) {
    Remove-Item $tempDir -Recurse -Force
}
New-Item -ItemType Directory -Path $tempDir | Out-Null

foreach ($shim in $shimPackages) {
    Write-Host "`nCreating shim: $($shim.PackageId)" -ForegroundColor Cyan
    
    $packageDir = Join-Path $tempDir $shim.PackageId
    New-Item -ItemType Directory -Path $packageDir | Out-Null
    
    # Create nuspec file
    $nuspecContent = @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>$($shim.PackageId)</id>
    <version>$Version-granville-shim</version>
    <authors>Granville</authors>
    <description>$($shim.Description)</description>
    <requireLicenseAcceptance>false</requireLicenseAcceptance>
    <license type="expression">MIT</license>
    <dependencies>
      <group targetFramework=".NETStandard2.0">
        <dependency id="$($shim.TargetPackage)" version="$Version" />
      </group>
    </dependencies>
  </metadata>
  <files>
    <!-- Empty package, just dependencies -->
  </files>
</package>
"@
    
    $nuspecPath = Join-Path $packageDir "$($shim.PackageId).nuspec"
    Set-Content -Path $nuspecPath -Value $nuspecContent -NoNewline
    
    # Pack the shim
    & "C:\ProgramData\chocolatey\lib\cs-script\tools\cs-script\lib\nuget.exe" pack $nuspecPath -OutputDirectory "../../Artifacts/Release" -NoPackageAnalysis
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  ✓ Created $($shim.PackageId).$Version-granville-shim.nupkg" -ForegroundColor Green
    }
}

# Clean up
Remove-Item $tempDir -Recurse -Force

Write-Host "`nAnalyzer shim packages created successfully!" -ForegroundColor Green