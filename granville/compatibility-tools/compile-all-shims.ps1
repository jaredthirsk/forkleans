#!/usr/bin/env pwsh

$ErrorActionPreference = "Stop"

Write-Host "Compiling all shims..." -ForegroundColor Cyan

# Assembly compilation configurations
$assemblies = @(
    @{
        "Name" = "Orleans.Core.Abstractions"
        "TargetFramework" = "net8.0"
        "References" = @(
            "../../../src/Orleans.Core.Abstractions/bin/Release/net8.0/Granville.Orleans.Core.Abstractions.dll"
        )
    },
    @{
        "Name" = "Orleans.Core"
        "TargetFramework" = "net8.0"
        "References" = @(
            "../../../src/Orleans.Core/bin/Release/net8.0/Granville.Orleans.Core.dll",
            "../../../src/Orleans.Core.Abstractions/bin/Release/net8.0/Granville.Orleans.Core.Abstractions.dll"
        )
    },
    @{
        "Name" = "Orleans.Runtime"
        "TargetFramework" = "net8.0"
        "References" = @(
            "../../../src/Orleans.Runtime/bin/Release/net8.0/Granville.Orleans.Runtime.dll",
            "../../../src/Orleans.Core/bin/Release/net8.0/Granville.Orleans.Core.dll",
            "../../../src/Orleans.Core.Abstractions/bin/Release/net8.0/Granville.Orleans.Core.Abstractions.dll"
        )
    },
    @{
        "Name" = "Orleans.Serialization.Abstractions"
        "TargetFramework" = "netstandard2.0"
        "References" = @(
            "../../../src/Orleans.Serialization.Abstractions/bin/Release/netstandard2.0/Granville.Orleans.Serialization.Abstractions.dll"
        )
    },
    @{
        "Name" = "Orleans.Serialization"
        "TargetFramework" = "net8.0"
        "References" = @(
            "../../../src/Orleans.Serialization/bin/Release/net8.0/Granville.Orleans.Serialization.dll",
            "../../../src/Orleans.Serialization.Abstractions/bin/Release/netstandard2.0/Granville.Orleans.Serialization.Abstractions.dll"
        )
    },
    @{
        "Name" = "Orleans.CodeGenerator"
        "TargetFramework" = "netstandard2.0"
        "References" = @(
            "../../../src/Orleans.CodeGenerator/bin/Release/netstandard2.0/Granville.Orleans.CodeGenerator.dll"
        )
    },
    @{
        "Name" = "Orleans.Analyzers"
        "TargetFramework" = "netstandard2.0"
        "References" = @(
            "../../../src/Orleans.Analyzers/bin/Release/netstandard2.0/Granville.Orleans.Analyzers.dll"
        )
    }
)

foreach ($assembly in $assemblies) {
    $assemblyName = $assembly.Name
    $targetFramework = $assembly.TargetFramework
    $references = $assembly.References
    
    Write-Host "Compiling $assemblyName..." -ForegroundColor Yellow
    
    # Create temporary directory for compilation
    $tempDir = "temp-$assemblyName-compile"
    if (Test-Path $tempDir) {
        Remove-Item -Path $tempDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
    
    # Generate project file
    $projectContent = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>$targetFramework</TargetFramework>
    <AssemblyName>$assemblyName</AssemblyName>
    <RootNamespace>$($assemblyName.Replace('.', '_'))</RootNamespace>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../shims-proper/$assemblyName.cs" />
  </ItemGroup>
  <ItemGroup>
"@
    
    # Add references
    foreach ($reference in $references) {
        $projectContent += @"
    <Reference Include="$([System.IO.Path]::GetFileNameWithoutExtension($reference))">
      <HintPath>$reference</HintPath>
      <Private>false</Private>
    </Reference>
"@
    }
    
    $projectContent += @"
  </ItemGroup>
</Project>
"@
    
    $projectContent | Out-File -FilePath "$tempDir/$assemblyName.csproj" -Encoding UTF8
    
    # Build the project
    Push-Location $tempDir
    try {
        Write-Host "  Restoring..." -ForegroundColor Gray
        & dotnet-win restore 2>&1 | Out-Host
        
        if ($LASTEXITCODE -ne 0) {
            Write-Host "✗ Failed to restore $assemblyName" -ForegroundColor Red
            exit 1
        }
        
        Write-Host "  Building..." -ForegroundColor Gray
        & dotnet-win build -c Release 2>&1 | Out-Host
        
        if ($LASTEXITCODE -ne 0) {
            Write-Host "✗ Failed to build $assemblyName" -ForegroundColor Red
            exit 1
        }
        
        # Copy the output
        $outputPath = "bin/Release/$targetFramework/$assemblyName.dll"
        if (Test-Path $outputPath) {
            Copy-Item -Path $outputPath -Destination "../shims-proper/$assemblyName.dll" -Force
            Write-Host "✓ Successfully compiled $assemblyName" -ForegroundColor Green
        } else {
            Write-Host "✗ Output file not found: $outputPath" -ForegroundColor Red
            exit 1
        }
        
    } finally {
        Pop-Location
    }
    
    # Cleanup
    Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "`n✓ All shims compiled successfully!" -ForegroundColor Green