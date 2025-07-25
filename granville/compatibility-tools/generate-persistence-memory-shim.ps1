#!/usr/bin/env pwsh

$ErrorActionPreference = "Stop"

# Create shim-packages directory if it doesn't exist
$shimPackageDir = "./shim-packages"
if (!(Test-Path $shimPackageDir)) {
    New-Item -ItemType Directory -Path $shimPackageDir | Out-Null
}

# Step 1: Generate type-forwarding shim
Write-Host "Generating shim for Orleans.Persistence.Memory..." -ForegroundColor Yellow

$sourceAssembly = "../../src/Orleans.Persistence.Memory/bin/Release/net8.0/Granville.Orleans.Persistence.Memory.dll"
$targetPath = "shims-proper/Orleans.Persistence.Memory.dll"

# Ensure source exists
if (!(Test-Path $sourceAssembly)) {
    Write-Error "Source assembly not found: $sourceAssembly"
}

# Load the assembly
Add-Type -AssemblyName System.Reflection.Emit
$assembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path $sourceAssembly))

# Create assembly builder
$assemblyName = New-Object System.Reflection.AssemblyName("Orleans.Persistence.Memory")
$assemblyBuilder = [System.Reflection.Emit.AssemblyBuilder]::DefineDynamicAssembly($assemblyName, [System.Reflection.Emit.AssemblyBuilderAccess]::RunAndCollect)
$moduleBuilder = $assemblyBuilder.DefineDynamicModule("Orleans.Persistence.Memory")

# Get all public types
$types = $assembly.GetExportedTypes()
Write-Host "  Found $($types.Count) types to forward" -ForegroundColor Cyan

# Add type forwards
foreach ($type in $types) {
    if (-not $type.IsNested) {
        # Create TypeForwardedTo attribute
        $ctorInfo = [System.Runtime.CompilerServices.TypeForwardedToAttribute].GetConstructor(@([Type]))
        $attrBuilder = New-Object System.Reflection.Emit.CustomAttributeBuilder($ctorInfo, @($type))
        $assemblyBuilder.SetCustomAttribute($attrBuilder)
    }
}

# Save the assembly
$assemblyPath = [System.IO.Path]::GetFullPath($targetPath)
$assemblyDir = [System.IO.Path]::GetDirectoryName($assemblyPath)
if (!(Test-Path $assemblyDir)) {
    New-Item -ItemType Directory -Path $assemblyDir -Force | Out-Null
}

# Create a simple C# file with type forwards and compile it
$csContent = @"
using System.Runtime.CompilerServices;

// Assembly: Orleans.Persistence.Memory
// Version: 9.1.2.0

"@

foreach ($type in $types) {
    if (-not $type.IsNested) {
        $csContent += "[assembly: TypeForwardedTo(typeof($($type.FullName)))]`n"
    }
}

$csPath = "shims-proper/Orleans.Persistence.Memory.cs"
$csContent | Out-File -FilePath $csPath -Encoding UTF8

# Compile the shim
Write-Host "  Compiling shim..." -ForegroundColor Cyan
$references = @(
    "-r:$sourceAssembly",
    "-r:../../src/Orleans.Core/bin/Release/net8.0/Granville.Orleans.Core.dll",
    "-r:../../src/Orleans.Runtime/bin/Release/net8.0/Granville.Orleans.Runtime.dll"
)

$compileCmd = "csc -target:library -out:`"$targetPath`" $($references -join ' ') `"$csPath`""
Invoke-Expression $compileCmd 2>&1 | Out-Null

if (Test-Path $targetPath) {
    Write-Host "  ✓ Successfully generated Orleans.Persistence.Memory.dll" -ForegroundColor Green
} else {
    Write-Error "Failed to generate shim"
}

# Step 2: Create NuGet package
Write-Host "`nCreating Microsoft.Orleans.Persistence.Memory shim package..." -ForegroundColor Yellow

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
    <description>Type-forwarding shim for Microsoft.Orleans.Persistence.Memory. Redirects to Granville.Orleans.Persistence.Memory.</description>
    <dependencies>
      <group targetFramework="net8.0">
        <dependency id="Granville.Orleans.Persistence.Memory" version="9.1.2.146" />
      </group>
    </dependencies>
  </metadata>
  <files>
    <file src="$targetPath" target="lib\net8.0\" />
  </files>
</package>
"@

$nuspecPath = "$shimPackageDir/Microsoft.Orleans.Persistence.Memory.nuspec"
$nuspecContent | Out-File -FilePath $nuspecPath -Encoding UTF8

# Pack the package
& nuget pack $nuspecPath -OutputDirectory "../../Artifacts/Release"

Write-Host "`n✓ Shim package created successfully!" -ForegroundColor Green