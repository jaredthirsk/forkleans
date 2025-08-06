#!/usr/bin/env dotnet-script
#r "nuget: Granville.Orleans.Serialization, 9.1.2.189"
#r "nuget: Granville.Orleans.Serialization.Abstractions, 9.1.2.189"
#r "nuget: Granville.Orleans.Core, 9.1.2.189"
#r "nuget: Granville.Orleans.Core.Abstractions, 9.1.2.189"
#r "nuget: Microsoft.Extensions.DependencyInjection, 8.0.1"
#r "nuget: Microsoft.Extensions.Logging.Console, 8.0.1"
#r "nuget: Microsoft.Extensions.Logging.Abstractions, 8.0.2"

using System;
using System.Buffers;
using Orleans.Serialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Session;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.TypeSystem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Test Orleans serialization
var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
services.AddSerializer();

var provider = services.BuildServiceProvider();
var serializer = provider.GetRequiredService<Serializer>();
var sessionPool = provider.GetRequiredService<SerializerSessionPool>();
var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
var logger = loggerFactory.CreateLogger("SerializationTest");

// Test 1: Serialize a string directly with session
{
    var testString = "c35a081a-b977-4bc6-8e7d-94a57f15f962";
    var writer = new ArrayBufferWriter<byte>();
    
    using var session = sessionPool.GetSession();
    serializer.Serialize(testString, writer, session);
    var bytes = writer.WrittenMemory.ToArray();
    
    logger.LogInformation("Test 1 - Direct string serialization with session:");
    logger.LogInformation("  Input: {Input}", testString);
    logger.LogInformation("  Serialized to {Length} bytes: {Hex}", bytes.Length, Convert.ToHexString(bytes));
    
    // Deserialize with fresh session
    using var deserSession = sessionPool.GetSession();
    var memory = new ReadOnlyMemory<byte>(bytes);
    var deserialized = serializer.Deserialize<string>(memory, deserSession);
    logger.LogInformation("  Deserialized: {Result}", deserialized);
}

// Test 2: Serialize a string with fresh session each time
{
    var testString = "c35a081a-b977-4bc6-8e7d-94a57f15f962";
    
    // Create a brand new session to avoid references
    var typeCodec = provider.GetRequiredService<TypeCodec>();
    var wellKnownTypes = provider.GetRequiredService<WellKnownTypeCollection>();
    var codecProvider = provider.GetRequiredService<CodecProvider>();
    
    using var freshSession = new SerializerSession(typeCodec, wellKnownTypes, codecProvider);
    
    var writer = new ArrayBufferWriter<byte>();
    serializer.Serialize(testString, writer, freshSession);
    var bytes = writer.WrittenMemory.ToArray();
    
    logger.LogInformation("\nTest 2 - String serialization with FRESH session:");
    logger.LogInformation("  Input: {Input}", testString);
    logger.LogInformation("  Serialized to {Length} bytes: {Hex}", bytes.Length, Convert.ToHexString(bytes));
}

// Test 3: Serialize an object array with a string
{
    var args = new object[] { "c35a081a-b977-4bc6-8e7d-94a57f15f962" };
    
    var typeCodec = provider.GetRequiredService<TypeCodec>();
    var wellKnownTypes = provider.GetRequiredService<WellKnownTypeCollection>();
    var codecProvider = provider.GetRequiredService<CodecProvider>();
    
    using var freshSession = new SerializerSession(typeCodec, wellKnownTypes, codecProvider);
    
    var writer = new ArrayBufferWriter<byte>();
    serializer.Serialize(args, writer, freshSession);
    var bytes = writer.WrittenMemory.ToArray();
    
    logger.LogInformation("\nTest 3 - Object array serialization with FRESH session:");
    logger.LogInformation("  Input: object[] {{ \"{Input}\" }}", args[0]);
    logger.LogInformation("  Serialized to {Length} bytes: {Hex}", bytes.Length, Convert.ToHexString(bytes));
    
    // Deserialize with fresh session
    using var deserSession = new SerializerSession(typeCodec, wellKnownTypes, codecProvider);
    var memory = new ReadOnlyMemory<byte>(bytes);
    var deserialized = serializer.Deserialize<object[]>(memory, deserSession);
    logger.LogInformation("  Deserialized: {Count} items", deserialized?.Length ?? 0);
    if (deserialized?.Length > 0)
    {
        logger.LogInformation("  Item[0]: Type={Type}, Value={Value}", 
            deserialized[0]?.GetType()?.Name ?? "null", 
            deserialized[0]?.ToString() ?? "null");
    }
}

// Test 4: What does a null array serialize to?
{
    var args = new object[] { null };
    
    var typeCodec = provider.GetRequiredService<TypeCodec>();
    var wellKnownTypes = provider.GetRequiredService<WellKnownTypeCollection>();
    var codecProvider = provider.GetRequiredService<CodecProvider>();
    
    using var freshSession = new SerializerSession(typeCodec, wellKnownTypes, codecProvider);
    
    var writer = new ArrayBufferWriter<byte>();
    serializer.Serialize(args, writer, freshSession);
    var bytes = writer.WrittenMemory.ToArray();
    
    logger.LogInformation("\nTest 4 - Object array with null:");
    logger.LogInformation("  Input: object[] {{ null }}");
    logger.LogInformation("  Serialized to {Length} bytes: {Hex}", bytes.Length, Convert.ToHexString(bytes));
}