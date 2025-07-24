#!/usr/bin/env pwsh

# Create Microsoft.Orleans.Analyzers shim package
$spec = @'
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd">
  <metadata>
    <id>Microsoft.Orleans.Analyzers</id>
    <version>9.1.2.126-granville-shim</version>
    <title>Microsoft Orleans - Analyzers [Granville Shim]</title>
    <authors>Microsoft</authors>
    <description>Type-forwarding shim package that redirects to Granville.Orleans.Analyzers. This is not an official Microsoft package.</description>
    <dependencies>
      <dependency id="Granville.Orleans.Analyzers" version="9.1.2.126" />
    </dependencies>
  </metadata>
  <files>
    <file src="../../src/Orleans.Analyzers/bin/Release/netstandard2.0/Orleans.Analyzers.dll" target="analyzers/dotnet/cs/Orleans.Analyzers.dll" />
  </files>
</package>
'@

$spec | Out-File -FilePath 'Microsoft.Orleans.Analyzers.nuspec' -Encoding utf8
./nuget.exe pack Microsoft.Orleans.Analyzers.nuspec -OutputDirectory ../../Artifacts/Release -Verbosity quiet
Remove-Item Microsoft.Orleans.Analyzers.nuspec
Write-Host "Created Microsoft.Orleans.Analyzers.9.1.2.126-granville-shim.nupkg"

# Create Microsoft.Orleans.CodeGenerator shim package
$spec2 = @'
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd">
  <metadata>
    <id>Microsoft.Orleans.CodeGenerator</id>
    <version>9.1.2.126-granville-shim</version>
    <title>Microsoft Orleans - Code Generator [Granville Shim]</title>
    <authors>Microsoft</authors>
    <description>Type-forwarding shim package that redirects to Granville.Orleans.CodeGenerator. This is not an official Microsoft package.</description>
    <dependencies>
      <dependency id="Granville.Orleans.CodeGenerator" version="9.1.2.126" />
    </dependencies>
  </metadata>
  <files>
    <file src="../../src/Orleans.CodeGenerator/bin/Release/netstandard2.0/Orleans.CodeGenerator.dll" target="build/Orleans.CodeGenerator.dll" />
    <file src="../../src/Orleans.CodeGenerator/bin/Release/netstandard2.0/Orleans.CodeGenerator.props" target="build/Microsoft.Orleans.CodeGenerator.props" />
  </files>
</package>
'@

$spec2 | Out-File -FilePath 'Microsoft.Orleans.CodeGenerator.nuspec' -Encoding utf8
./nuget.exe pack Microsoft.Orleans.CodeGenerator.nuspec -OutputDirectory ../../Artifacts/Release -Verbosity quiet
Remove-Item Microsoft.Orleans.CodeGenerator.nuspec
Write-Host "Created Microsoft.Orleans.CodeGenerator.9.1.2.126-granville-shim.nupkg"