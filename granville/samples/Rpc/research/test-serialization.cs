#!/usr/bin/env dotnet-script
#r "nuget: Granville.Orleans.Serialization, 9.1.2.189"
#r "nuget: Granville.Orleans.Core, 9.1.2.189"
#r "nuget: Microsoft.Extensions.DependencyInjection, 8.0.1"
#r "nuget: Microsoft.Extensions.Logging.Console, 8.0.1"

using System;
using System.Buffers;
using Orleans.Serialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Session;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Test Orleans serialization
var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
services.AddSerializer();

var provider = services.BuildServiceProvider();
var serializer = provider.GetRequiredService<Serializer>();
var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
var logger = loggerFactory.CreateLogger("SerializationTest");

// Test 1: Serialize a string directly
{
    var testString = "c35a081a-b977-4bc6-8e7d-94a57f15f962";
    var writer = new ArrayBufferWriter<byte>();
    serializer.Serialize(testString, writer);
    var bytes = writer.WrittenMemory.ToArray();
    
    logger.LogInformation("Test 1 - Direct string serialization:");
    logger.LogInformation("  Input: {Input}", testString);
    logger.LogInformation("  Serialized to {Length} bytes: {Hex}", bytes.Length, Convert.ToHexString(bytes));
    
    // Deserialize
    var reader = Reader.Create(bytes, provider);
    var deserialized = serializer.Deserialize<string>(ref reader);
    logger.LogInformation("  Deserialized: {Result}", deserialized);
}

// Test 2: Serialize an object array with a string
{
    var args = new object[] { "c35a081a-b977-4bc6-8e7d-94a57f15f962" };
    var writer = new ArrayBufferWriter<byte>();
    serializer.Serialize(args, writer);
    var bytes = writer.WrittenMemory.ToArray();
    
    logger.LogInformation("\nTest 2 - Object array serialization:");
    logger.LogInformation("  Input: object[] {{ \"{Input}\" }}", args[0]);
    logger.LogInformation("  Serialized to {Length} bytes: {Hex}", bytes.Length, Convert.ToHexString(bytes));
    
    // Deserialize
    var reader = Reader.Create(bytes, provider);
    var deserialized = serializer.Deserialize<object[]>(ref reader);
    logger.LogInformation("  Deserialized: {Count} items", deserialized?.Length ?? 0);
    if (deserialized?.Length > 0)
    {
        logger.LogInformation("  Item[0]: {Value}", deserialized[0]);
    }
}

// Test 3: What does a null array serialize to?
{
    var args = new object[] { null };
    var writer = new ArrayBufferWriter<byte>();
    serializer.Serialize(args, writer);
    var bytes = writer.WrittenMemory.ToArray();
    
    logger.LogInformation("\nTest 3 - Object array with null:");
    logger.LogInformation("  Input: object[] {{ null }}");
    logger.LogInformation("  Serialized to {Length} bytes: {Hex}", bytes.Length, Convert.ToHexString(bytes));
}