#!/usr/bin/env pwsh

# Test script to understand GrainInterfaceType format

Write-Host "Building test program to understand GrainInterfaceType format..." -ForegroundColor Green

# Create a simple test program
$testProgram = @'
using System;
using Orleans.Runtime;
using Orleans.Metadata;
using Orleans.Serialization.TypeSystem;
using Shooter.Shared.RpcInterfaces;

class Program
{
    static void Main()
    {
        try
        {
            var interfaceType = typeof(IGameRpcGrain);
            Console.WriteLine($"Interface Type: {interfaceType}");
            Console.WriteLine($"Interface Type FullName: {interfaceType.FullName}");
            Console.WriteLine($"Interface Type AssemblyQualifiedName: {interfaceType.AssemblyQualifiedName}");
            
            // Try to format it as Orleans would
            var formatted = RuntimeTypeNameFormatter.Format(interfaceType);
            Console.WriteLine($"RuntimeTypeNameFormatter.Format: {formatted}");
            
            // Create a GrainInterfaceType
            var grainInterfaceType = new GrainInterfaceType(formatted);
            Console.WriteLine($"GrainInterfaceType.ToString(): {grainInterfaceType}");
            
            // Check what we're looking for
            var grainTypeStr = grainInterfaceType.ToString();
            Console.WriteLine($"Contains 'Rpc': {grainTypeStr.Contains("Rpc")}");
            Console.WriteLine($"Contains 'Shooter': {grainTypeStr.Contains("Shooter")}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
        }
    }
}
'@

# Save the test program
$testProgramPath = "$PSScriptRoot/TestInterfaceFormat/TestInterfaceFormat.cs"
$testDir = Split-Path -Parent $testProgramPath
if (!(Test-Path $testDir)) {
    New-Item -ItemType Directory -Path $testDir -Force | Out-Null
}
$testProgram | Out-File -FilePath $testProgramPath -Encoding UTF8

# Create a simple project file
$projectFile = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Shooter.Shared\Shooter.Shared.csproj" />
    <PackageReference Include="Granville.Orleans.Core.Abstractions" />
    <PackageReference Include="Granville.Orleans.Serialization" />
  </ItemGroup>
</Project>
'@

$projectFile | Out-File -FilePath "$testDir/TestInterfaceFormat.csproj" -Encoding UTF8

Write-Host "Running test program..." -ForegroundColor Yellow
Push-Location $testDir
try {
    dotnet-win run
}
finally {
    Pop-Location
}

Write-Host "Test complete!" -ForegroundColor Green