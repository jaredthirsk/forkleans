#!/usr/bin/env dotnet-script

using System;
using System.Linq;
using System.Reflection;

var assemblyPath = @"G:\forks\orleans\src\Orleans.Serialization\bin\Release\net8.0\Granville.Orleans.Serialization.dll";

try
{
    Console.WriteLine($"Loading assembly from: {assemblyPath}");
    
    // Load the assembly
    var assembly = Assembly.LoadFrom(assemblyPath);
    
    Console.WriteLine($"Successfully loaded assembly: {assembly.FullName}");
    Console.WriteLine();
    
    // Get all custom attributes on the assembly
    var attributes = assembly.GetCustomAttributes();
    
    Console.WriteLine("All assembly attributes:");
    foreach (var attr in attributes)
    {
        Console.WriteLine($"  - {attr.GetType().FullName}");
    }
    
    Console.WriteLine();
    
    // Check specifically for ApplicationPart attribute
    var applicationPartAttributes = attributes.Where(a => a.GetType().Name == "ApplicationPartAttribute" || 
                                                          a.GetType().FullName.Contains("ApplicationPart")).ToList();
    
    if (applicationPartAttributes.Any())
    {
        Console.WriteLine("✓ Found ApplicationPart attribute(s):");
        foreach (var attr in applicationPartAttributes)
        {
            Console.WriteLine($"  - {attr.GetType().FullName}");
            
            // Try to get the value if it has one
            var valueProperty = attr.GetType().GetProperty("Value");
            if (valueProperty != null)
            {
                var value = valueProperty.GetValue(attr);
                Console.WriteLine($"    Value: {value}");
            }
        }
    }
    else
    {
        Console.WriteLine("✗ ApplicationPart attribute NOT found on assembly");
    }
    
    // Also check for Orleans-specific attributes
    Console.WriteLine();
    Console.WriteLine("Orleans-specific attributes:");
    var orleansAttributes = attributes.Where(a => a.GetType().FullName.Contains("Orleans")).ToList();
    foreach (var attr in orleansAttributes)
    {
        Console.WriteLine($"  - {attr.GetType().FullName}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
}