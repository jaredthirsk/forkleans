#!/usr/bin/env dotnet-script
#r "nuget: Granville.Orleans.Core, 9.1.2.189"
#r "nuget: Granville.Orleans.CodeGenerator, 9.1.2.189"
#r "nuget: Microsoft.Extensions.Logging.Console, 8.0.1"

using System;
using System.Reflection;
using Orleans;
using Orleans.Runtime;
using Microsoft.Extensions.Logging;

// Simulate what Orleans generates
public class TestInvokable : Orleans.Runtime.Request
{
    public string arg0;
    
    public override int GetArgumentCount() => 1;
    public override object GetArgument(int index)
    {
        // This is what's broken in Orleans - it throws instead of returning null
        throw new NotSupportedException("GetArgument is not supported on this type");
    }
}

// Test the reflection workaround
var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
var logger = loggerFactory.CreateLogger("ReflectionTest");

var invokable = new TestInvokable { arg0 = "test-player-123" };

logger.LogInformation("Testing Orleans proxy argument extraction...");
logger.LogInformation("Invokable type: {Type}", invokable.GetType().FullName);
logger.LogInformation("Field arg0 value: {Value}", invokable.arg0);

// Try GetArgument
try
{
    var value = invokable.GetArgument(0);
    logger.LogInformation("GetArgument(0) returned: {Value}", value);
}
catch (Exception ex)
{
    logger.LogError("GetArgument(0) threw: {Exception}", ex.GetType().Name);
    
    // Use reflection workaround
    var field = invokable.GetType().GetField("arg0", 
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    
    if (field != null)
    {
        var reflectionValue = field.GetValue(invokable);
        logger.LogInformation("✅ Reflection workaround SUCCESS: Field value = {Value}", reflectionValue);
    }
    else
    {
        logger.LogError("❌ Reflection workaround FAILED: Field not found");
    }
}