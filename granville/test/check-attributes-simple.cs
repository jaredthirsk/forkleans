#!/usr/bin/env dotnet-script

using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.IO;

var assemblyPath = @"G:\forks\orleans\src\Orleans.Serialization\bin\Release\net8.0\Granville.Orleans.Serialization.dll";

try
{
    Console.WriteLine($"Checking assembly at: {assemblyPath}");
    Console.WriteLine();
    
    using (var stream = File.OpenRead(assemblyPath))
    using (var peReader = new PEReader(stream))
    {
        var metadataReader = peReader.GetMetadataReader();
        
        Console.WriteLine("Assembly Custom Attributes:");
        
        bool foundApplicationPart = false;
        
        foreach (var customAttributeHandle in metadataReader.CustomAttributes)
        {
            var customAttribute = metadataReader.GetCustomAttribute(customAttributeHandle);
            
            // Check if this attribute is on the assembly
            if (customAttribute.Parent.Kind == HandleKind.AssemblyDefinition)
            {
                var ctorHandle = customAttribute.Constructor;
                string typeName = "";
                
                if (ctorHandle.Kind == HandleKind.MemberReference)
                {
                    var memberRef = metadataReader.GetMemberReference((MemberReferenceHandle)ctorHandle);
                    if (memberRef.Parent.Kind == HandleKind.TypeReference)
                    {
                        var typeRef = metadataReader.GetTypeReference((TypeReferenceHandle)memberRef.Parent);
                        typeName = metadataReader.GetString(typeRef.Name);
                        var namespaceName = metadataReader.GetString(typeRef.Namespace);
                        if (!string.IsNullOrEmpty(namespaceName))
                        {
                            typeName = namespaceName + "." + typeName;
                        }
                    }
                }
                else if (ctorHandle.Kind == HandleKind.MethodDefinition)
                {
                    var methodDef = metadataReader.GetMethodDefinition((MethodDefinitionHandle)ctorHandle);
                    var typeDef = metadataReader.GetTypeDefinition(methodDef.GetDeclaringType());
                    typeName = metadataReader.GetString(typeDef.Name);
                    var namespaceName = metadataReader.GetString(typeDef.Namespace);
                    if (!string.IsNullOrEmpty(namespaceName))
                    {
                        typeName = namespaceName + "." + typeName;
                    }
                }
                
                Console.WriteLine($"  - {typeName}");
                
                if (typeName.Contains("ApplicationPart"))
                {
                    foundApplicationPart = true;
                }
            }
        }
        
        Console.WriteLine();
        if (foundApplicationPart)
        {
            Console.WriteLine("✓ ApplicationPart attribute FOUND on assembly");
        }
        else
        {
            Console.WriteLine("✗ ApplicationPart attribute NOT found on assembly");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
}