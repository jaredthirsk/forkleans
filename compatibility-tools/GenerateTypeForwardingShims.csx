#!/usr/bin/env dotnet-script
#r "nuget: System.Reflection.MetadataLoadContext, 8.0.1"

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.MetadataLoadContext;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Text;

// Configuration
var granvilleAssemblyPath = Args.FirstOrDefault() ?? throw new ArgumentException("Please provide path to Granville Orleans assembly");
var outputPath = Args.Skip(1).FirstOrDefault() ?? Path.GetDirectoryName(granvilleAssemblyPath);
var microsoftAssemblyName = Path.GetFileName(granvilleAssemblyPath).Replace("Granville.", "Microsoft.");

Console.WriteLine($"Generating type forwarding shim:");
Console.WriteLine($"  From: {granvilleAssemblyPath}");
Console.WriteLine($"  To: {Path.Combine(outputPath, microsoftAssemblyName)}");

if (!File.Exists(granvilleAssemblyPath))
{
    throw new FileNotFoundException($"Assembly not found: {granvilleAssemblyPath}");
}

// Get assembly information
var granvilleAssembly = Assembly.LoadFrom(granvilleAssemblyPath);
var version = granvilleAssembly.GetName().Version;
var publicKeyToken = granvilleAssembly.GetName().GetPublicKeyToken();

// Extract all public types
var publicTypes = GetPublicTypes(granvilleAssembly);
Console.WriteLine($"Found {publicTypes.Count} public types to forward");

// Generate the shim assembly
GenerateShimAssembly(microsoftAssemblyName, granvilleAssembly.GetName(), publicTypes, outputPath);

Console.WriteLine($"Successfully generated shim: {Path.Combine(outputPath, microsoftAssemblyName)}");

List<Type> GetPublicTypes(Assembly assembly)
{
    var types = new List<Type>();
    
    try
    {
        foreach (var type in assembly.GetExportedTypes())
        {
            // Skip compiler-generated types
            if (type.Name.Contains("<") || type.Name.Contains(">"))
                continue;
                
            types.Add(type);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Warning: Error getting exported types: {ex.Message}");
    }
    
    return types;
}

void GenerateShimAssembly(string shimAssemblyName, AssemblyName targetAssemblyName, List<Type> typesToForward, string outputDir)
{
    var shimName = new AssemblyName(Path.GetFileNameWithoutExtension(shimAssemblyName))
    {
        Version = targetAssemblyName.Version
    };
    
    var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(shimName, AssemblyBuilderAccess.Save);
    var moduleBuilder = assemblyBuilder.DefineDynamicModule(shimName.Name, shimAssemblyName);
    
    // Add assembly attributes
    var descriptionCtor = typeof(System.Reflection.AssemblyDescriptionAttribute).GetConstructor(new[] { typeof(string) });
    assemblyBuilder.SetCustomAttribute(new CustomAttributeBuilder(descriptionCtor, new object[] { "Type forwarding shim for Granville Orleans compatibility" }));
    
    var companyCtor = typeof(System.Reflection.AssemblyCompanyAttribute).GetConstructor(new[] { typeof(string) });
    assemblyBuilder.SetCustomAttribute(new CustomAttributeBuilder(companyCtor, new object[] { "Granville RPC" }));
    
    var productCtor = typeof(System.Reflection.AssemblyProductAttribute).GetConstructor(new[] { typeof(string) });
    assemblyBuilder.SetCustomAttribute(new CustomAttributeBuilder(productCtor, new object[] { "Granville Orleans Compatibility Shims" }));
    
    // Add type forwards
    foreach (var type in typesToForward)
    {
        try
        {
            var forwardAttrCtor = typeof(System.Runtime.CompilerServices.TypeForwardedToAttribute).GetConstructor(new[] { typeof(Type) });
            moduleBuilder.SetCustomAttribute(new CustomAttributeBuilder(forwardAttrCtor, new object[] { type }));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not forward type {type.FullName}: {ex.Message}");
        }
    }
    
    // Save the assembly
    assemblyBuilder.Save(shimAssemblyName);
    
    // Move to output directory if needed
    if (outputDir != ".")
    {
        var sourcePath = Path.Combine(Environment.CurrentDirectory, shimAssemblyName);
        var destPath = Path.Combine(outputDir, shimAssemblyName);
        
        if (File.Exists(destPath))
            File.Delete(destPath);
            
        File.Move(sourcePath, destPath);
    }
}

// Usage example
Console.WriteLine("\nUsage:");
Console.WriteLine("  dotnet-script GenerateTypeForwardingShims.csx <path-to-granville-assembly> [output-directory]");
Console.WriteLine("\nExample:");
Console.WriteLine("  dotnet-script GenerateTypeForwardingShims.csx bin/Release/net8.0/Granville.Orleans.Core.dll shims/");