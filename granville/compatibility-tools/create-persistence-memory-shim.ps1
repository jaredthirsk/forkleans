#!/usr/bin/env pwsh

Write-Host "Creating Microsoft.Orleans.Persistence.Memory shim package..." -ForegroundColor Yellow

# The shim package just needs to redirect to Granville version
$nuspecContent = @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>Microsoft.Orleans.Persistence.Memory</id>
    <version>9.1.2.146-granville-shim</version>
    <authors>Microsoft</authors>
    <owners>Microsoft</owners>
    <requireLicenseAcceptance>false</requireLicenseAcceptance>
    <license type="expression">MIT</license>
    <projectUrl>https://github.com/dotnet/orleans</projectUrl>
    <description>Granville shim package for Microsoft.Orleans.Persistence.Memory. This package depends on Granville.Orleans.Persistence.Memory and allows compatibility with code expecting Microsoft.Orleans packages.</description>
    <dependencies>
      <group targetFramework="net8.0">
        <dependency id="Granville.Orleans.Persistence.Memory" version="9.1.2.146" />
      </group>
    </dependencies>
  </metadata>
</package>
"@

$nuspecPath = "Microsoft.Orleans.Persistence.Memory.nuspec"
$nuspecContent | Out-File -FilePath $nuspecPath -Encoding UTF8

# Pack the package (empty package with just dependencies)
& nuget.exe pack $nuspecPath -OutputDirectory "../../Artifacts/Release"

# Clean up
Remove-Item $nuspecPath

Write-Host "✓ Shim package created successfully!" -ForegroundColor Green