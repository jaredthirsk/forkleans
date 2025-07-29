#!/usr/bin/env dotnet-script
#r "nuget: Granville.Rpc.Client, 9.1.2.149"
#r "nuget: Granville.Orleans.Core, 9.1.2.149"
#r "nuget: Granville.Orleans.Serialization, 9.1.2.149"
#r "../Shooter.Shared/bin/Debug/net9.0/Shooter.Shared.dll"

using System;
using System.Reflection;
using Orleans;
using Orleans.Runtime;
using Shooter.Shared.RpcInterfaces;

Console.WriteLine("=== Testing Proxy Generation ===");

// Check if Shooter.Shared has Orleans code generation
var sharedAssembly = typeof(IGameRpcGrain).Assembly;
Console.WriteLine($"\nShooter.Shared Assembly: {sharedAssembly.FullName}");

// Look for generated proxy classes
var proxyTypes = sharedAssembly.GetTypes()
    .Where(t => t.Name.Contains("Proxy") || t.Name.Contains("Reference") || t.Name.Contains("Invoker"))
    .ToList();

Console.WriteLine($"\nFound {proxyTypes.Count} potential proxy types:");
foreach (var type in proxyTypes)
{
    Console.WriteLine($"  - {type.FullName}");
}

// Check for Orleans metadata providers
var metadataTypes = sharedAssembly.GetTypes()
    .Where(t => t.Name.Contains("Metadata") || t.Name.Contains("Manifest"))
    .ToList();

Console.WriteLine($"\nFound {metadataTypes.Count} metadata types:");
foreach (var type in metadataTypes)
{
    Console.WriteLine($"  - {type.FullName}");
}

// Check GrainInterfaceType
var interfaceType = typeof(IGameRpcGrain);
var grainInterfaceType = GrainInterfaceType.Create(interfaceType);
Console.WriteLine($"\nIGameRpcGrain GrainInterfaceType: {grainInterfaceType}");

// Check if we can find the interface in loaded assemblies
var allAssemblies = AppDomain.CurrentDomain.GetAssemblies();
Console.WriteLine($"\nTotal loaded assemblies: {allAssemblies.Length}");

var orleansAssemblies = allAssemblies
    .Where(a => a.FullName.Contains("Orleans") || a.FullName.Contains("Shooter"))
    .OrderBy(a => a.FullName);

Console.WriteLine("\nOrleans/Shooter assemblies:");
foreach (var asm in orleansAssemblies)
{
    Console.WriteLine($"  - {asm.FullName}");
}