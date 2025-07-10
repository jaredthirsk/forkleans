#!/usr/bin/env pwsh
# Create minimal packages for CodeGenerator and Analyzers

param(
    [string]$Configuration = "Release",
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

Write-Host "Creating CodeGenerator and Analyzers packages..." -ForegroundColor Green

# First build the projects
Write-Host "Building projects..." -ForegroundColor Cyan
dotnet build src/Orleans.CodeGenerator/Orleans.CodeGenerator.csproj -c $Configuration
dotnet build src/Orleans.Analyzers/Orleans.Analyzers.csproj -c $Configuration

# Create temporary directory
$tempDir = New-Item -ItemType Directory -Force -Path "temp-codegen-pack"

# Create Granville.Orleans.CodeGenerator package
Write-Host "`nCreating Granville.Orleans.CodeGenerator package..." -ForegroundColor Cyan
$codegenProjDir = "$tempDir/CodeGenerator"
New-Item -ItemType Directory -Force -Path "$codegenProjDir/analyzers/dotnet/cs" | Out-Null

# Copy the built DLL
Copy-Item "src/Orleans.CodeGenerator/bin/$Configuration/netstandard2.0/Granville.Orleans.CodeGenerator.dll" "$codegenProjDir/analyzers/dotnet/cs/"

# Create a minimal project file for packaging
$codegenProj = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <PackageId>Granville.Orleans.CodeGenerator</PackageId>
    <Version>$Version</Version>
    <Authors>Granville RPC Contributors</Authors>
    <Description>Code generator for Granville Orleans</Description>
    <DevelopmentDependency>true</DevelopmentDependency>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>
    <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
  </PropertyGroup>
  
  <ItemGroup>
    <None Include="analyzers/dotnet/cs/*.dll" Pack="true" PackagePath="analyzers/dotnet/cs" />
  </ItemGroup>
</Project>
"@

$codegenProj | Out-File "$codegenProjDir/Granville.Orleans.CodeGenerator.csproj" -Encoding UTF8

# Restore and pack it
dotnet restore "$codegenProjDir/Granville.Orleans.CodeGenerator.csproj"
dotnet pack "$codegenProjDir/Granville.Orleans.CodeGenerator.csproj" -c $Configuration -o Artifacts/Release --no-build

# Create Granville.Orleans.Analyzers package
Write-Host "`nCreating Granville.Orleans.Analyzers package..." -ForegroundColor Cyan
$analyzersProjDir = "$tempDir/Analyzers"
New-Item -ItemType Directory -Force -Path "$analyzersProjDir/analyzers/dotnet/cs" | Out-Null

# Copy the built DLL
Copy-Item "src/Orleans.Analyzers/bin/$Configuration/netstandard2.0/Granville.Orleans.Analyzers.dll" "$analyzersProjDir/analyzers/dotnet/cs/"

# Create a minimal project file for packaging
$analyzersProj = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <PackageId>Granville.Orleans.Analyzers</PackageId>
    <Version>$Version</Version>
    <Authors>Granville RPC Contributors</Authors>
    <Description>Analyzers for Granville Orleans</Description>
    <DevelopmentDependency>true</DevelopmentDependency>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>
    <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
  </PropertyGroup>
  
  <ItemGroup>
    <None Include="analyzers/dotnet/cs/*.dll" Pack="true" PackagePath="analyzers/dotnet/cs" />
  </ItemGroup>
</Project>
"@

$analyzersProj | Out-File "$analyzersProjDir/Granville.Orleans.Analyzers.csproj" -Encoding UTF8

# Restore and pack it
dotnet restore "$analyzersProjDir/Granville.Orleans.Analyzers.csproj"
dotnet pack "$analyzersProjDir/Granville.Orleans.Analyzers.csproj" -c $Configuration -o Artifacts/Release --no-build

# Clean up
Remove-Item -Recurse -Force $tempDir

Write-Host "`nPackaging complete!" -ForegroundColor Green
Write-Host "Packages created in Artifacts/Release/" -ForegroundColor Cyan