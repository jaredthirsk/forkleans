#!/usr/bin/env pwsh

# Create Microsoft.Orleans.CodeGenerator shim package
$spec = @'
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd">
  <metadata>
    <id>Microsoft.Orleans.CodeGenerator</id>
    <version>9.1.2.130-granville-shim</version>
    <title>Microsoft Orleans - Code Generator [Granville Shim]</title>
    <authors>Microsoft</authors>
    <description>Type-forwarding shim package that redirects to Granville.Orleans.CodeGenerator. This is not an official Microsoft package.</description>
    <dependencies>
      <dependency id="Granville.Orleans.CodeGenerator" version="9.1.2.130" />
    </dependencies>
  </metadata>
  <files>
    <file src="../../src/Orleans.CodeGenerator/bin/Release/netstandard2.0/Orleans.CodeGenerator.dll" target="build/Orleans.CodeGenerator.dll" />
    <file src="../../src/Orleans.CodeGenerator/build/Microsoft.Orleans.CodeGenerator.props" target="build/Microsoft.Orleans.CodeGenerator.props" />
    <file src="../../src/Orleans.CodeGenerator/buildMultiTargeting/Microsoft.Orleans.CodeGenerator.props" target="buildMultiTargeting/Microsoft.Orleans.CodeGenerator.props" />
    <file src="../../src/Orleans.CodeGenerator/buildTransitive/Microsoft.Orleans.CodeGenerator.props" target="buildTransitive/Microsoft.Orleans.CodeGenerator.props" />
  </files>
</package>
'@

$spec | Out-File -FilePath 'Microsoft.Orleans.CodeGenerator.nuspec' -Encoding utf8
./nuget.exe pack Microsoft.Orleans.CodeGenerator.nuspec -OutputDirectory ../../Artifacts/Release -Verbosity quiet
Remove-Item Microsoft.Orleans.CodeGenerator.nuspec
Write-Host "Created Microsoft.Orleans.CodeGenerator.9.1.2.126-granville-shim.nupkg"