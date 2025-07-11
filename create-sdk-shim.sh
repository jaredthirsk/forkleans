#!/bin/bash

# Create Microsoft.Orleans.Sdk shim package manually

cd /mnt/g/forks/orleans

# Create temp directory structure
rm -rf temp-sdk-shim
mkdir -p temp-sdk-shim/build

# Create props file
cat > temp-sdk-shim/build/Microsoft.Orleans.Sdk.props << 'EOF'
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
EOF

# Create nuspec
cat > temp-sdk-shim/Microsoft.Orleans.Sdk.nuspec << 'EOF'
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>Microsoft.Orleans.Sdk</id>
    <version>9.1.2.76-granville-shim</version>
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
        <dependency id="Granville.Orleans.Sdk" version="9.1.2.76" />
      </group>
    </dependencies>
  </metadata>
  <files>
    <file src="build/**" target="build/" />
  </files>
</package>
EOF

# Create the nupkg manually using zip
cd temp-sdk-shim
zip -r ../Microsoft.Orleans.Sdk.9.1.2.76-granville-shim.nupkg * 
cd ..
mv Microsoft.Orleans.Sdk.9.1.2.76-granville-shim.nupkg Artifacts/Release/

# Clean up
rm -rf temp-sdk-shim temp-manual-pack

echo "Created Microsoft.Orleans.Sdk.9.1.2.76-granville-shim.nupkg"