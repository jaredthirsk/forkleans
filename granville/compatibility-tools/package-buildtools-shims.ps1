#!/usr/bin/env pwsh
# Script to create shim packages for Microsoft.Orleans.CodeGenerator and Microsoft.Orleans.Analyzers
# These shims redirect to the Granville versions

param(
    [string]$Version = "9.1.2.80-granville-shim",
    [string]$OutputPath = "../../Artifacts/Release"
)

$ErrorActionPreference = "Stop"

# Ensure output directory exists
$OutputPath = Resolve-Path $OutputPath
if (!(Test-Path $OutputPath)) {
    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
}

Write-Host "Creating build tools shim packages..." -ForegroundColor Green

# Create temporary directory for package contents
$tempDir = New-Item -ItemType Directory -Path ([System.IO.Path]::GetTempPath() + [System.IO.Path]::GetRandomFileName()) -Force

try {
    # Microsoft.Orleans.CodeGenerator shim
    Write-Host "`nCreating Microsoft.Orleans.CodeGenerator shim..." -ForegroundColor Cyan
    
    $codeGenDir = Join-Path $tempDir "CodeGenerator"
    New-Item -ItemType Directory -Path $codeGenDir -Force | Out-Null
    
    # Create directory structure
    $buildDir = New-Item -ItemType Directory -Path (Join-Path $codeGenDir "build") -Force
    $buildTransitiveDir = New-Item -ItemType Directory -Path (Join-Path $codeGenDir "buildTransitive") -Force
    $buildMultiTargetingDir = New-Item -ItemType Directory -Path (Join-Path $codeGenDir "buildMultiTargeting") -Force
    
    # Create props file that disables Microsoft.Orleans.CodeGenerator (Granville already imported)
    $propsContent = @'
<Project>
  <!-- Microsoft.Orleans.CodeGenerator shim - does nothing because Granville.Orleans.CodeGenerator is already imported -->
  <!-- This prevents duplicate code generation -->
  <PropertyGroup>
    <Orleans_DesignTimeBuild>true</Orleans_DesignTimeBuild>
  </PropertyGroup>
</Project>
'@
    
    $propsContent | Out-File -FilePath (Join-Path $buildDir "Microsoft.Orleans.CodeGenerator.props") -Encoding UTF8
    $propsContent.Replace("\build\", "\buildTransitive\") | Out-File -FilePath (Join-Path $buildTransitiveDir "Microsoft.Orleans.CodeGenerator.props") -Encoding UTF8
    $propsContent.Replace("\build\", "\buildMultiTargeting\") | Out-File -FilePath (Join-Path $buildMultiTargetingDir "Microsoft.Orleans.CodeGenerator.props") -Encoding UTF8
    
    # Create nuspec file (use older schema for compatibility)
    $nuspecContent = @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2011/08/nuspec.xsd">
  <metadata>
    <id>Microsoft.Orleans.CodeGenerator</id>
    <version>$Version</version>
    <authors>Granville RPC Contributors</authors>
    <owners>Granville RPC Contributors</owners>
    <requireLicenseAcceptance>false</requireLicenseAcceptance>
    <licenseUrl>https://opensource.org/licenses/MIT</licenseUrl>
    <projectUrl>https://github.com/jaredthirsk/orleans</projectUrl>
    <description>Shim package that redirects to Granville.Orleans.CodeGenerator</description>
    <copyright>© Microsoft Corporation. All rights reserved.</copyright>
    <tags>Orleans Cloud-Computing Actor-Model Actors Distributed-Systems C# .NET granville-shim</tags>
    <developmentDependency>true</developmentDependency>
    <dependencies>
      <!-- No dependencies - this just disables Microsoft.Orleans.CodeGenerator -->
    </dependencies>
  </metadata>
  <files>
    <file src="build\**" target="build\" />
    <file src="buildTransitive\**" target="buildTransitive\" />
    <file src="buildMultiTargeting\**" target="buildMultiTargeting\" />
  </files>
</package>
"@
    
    $nuspecPath = Join-Path $codeGenDir "Microsoft.Orleans.CodeGenerator.nuspec"
    $nuspecContent | Out-File -FilePath $nuspecPath -Encoding UTF8
    
    # Pack using nuget_win (convert paths to Windows format)
    Push-Location $codeGenDir
    try {
        $winNuspecPath = $nuspecPath -replace '^/mnt/([a-z])/', '$1:/' -replace '/', '\'
        $winOutputPath = $OutputPath -replace '^/mnt/([a-z])/', '$1:/' -replace '/', '\'
        nuget_win pack $winNuspecPath -OutputDirectory $winOutputPath -NoPackageAnalysis
    } catch {
        Write-Error "Failed to pack Microsoft.Orleans.CodeGenerator: $_"
        exit 1
    }
    Pop-Location
    
    # Microsoft.Orleans.Analyzers shim
    Write-Host "`nCreating Microsoft.Orleans.Analyzers shim..." -ForegroundColor Cyan
    
    $analyzersDir = Join-Path $tempDir "Analyzers"
    New-Item -ItemType Directory -Path $analyzersDir -Force | Out-Null
    
    # Create nuspec file for Analyzers (use older schema for compatibility)
    $analyzersNuspecContent = @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2011/08/nuspec.xsd">
  <metadata>
    <id>Microsoft.Orleans.Analyzers</id>
    <version>$Version</version>
    <authors>Granville RPC Contributors</authors>
    <owners>Granville RPC Contributors</owners>
    <requireLicenseAcceptance>false</requireLicenseAcceptance>
    <licenseUrl>https://opensource.org/licenses/MIT</licenseUrl>
    <projectUrl>https://github.com/jaredthirsk/orleans</projectUrl>
    <description>Shim package that redirects to Granville.Orleans.Analyzers</description>
    <copyright>© Microsoft Corporation. All rights reserved.</copyright>
    <tags>Orleans Cloud-Computing Actor-Model Actors Distributed-Systems C# .NET granville-shim</tags>
    <developmentDependency>true</developmentDependency>
    <dependencies>
      <dependency id="Granville.Orleans.Analyzers" version="9.1.2.80" />
    </dependencies>
  </metadata>
</package>
"@
    
    $analyzersNuspecPath = Join-Path $analyzersDir "Microsoft.Orleans.Analyzers.nuspec"
    $analyzersNuspecContent | Out-File -FilePath $analyzersNuspecPath -Encoding UTF8
    
    # Pack using nuget_win (convert paths to Windows format)
    Push-Location $analyzersDir
    try {
        $winNuspecPath = $analyzersNuspecPath -replace '^/mnt/([a-z])/', '$1:/' -replace '/', '\'
        $winOutputPath = $OutputPath -replace '^/mnt/([a-z])/', '$1:/' -replace '/', '\'
        nuget_win pack $winNuspecPath -OutputDirectory $winOutputPath -NoPackageAnalysis
    } catch {
        Write-Error "Failed to pack Microsoft.Orleans.Analyzers: $_"
        exit 1
    }
    Pop-Location
    
    Write-Host "`nBuild tools shim packages created successfully!" -ForegroundColor Green
}
finally {
    # Clean up temp directory
    if (Test-Path $tempDir) {
        Remove-Item $tempDir -Recurse -Force
    }
}