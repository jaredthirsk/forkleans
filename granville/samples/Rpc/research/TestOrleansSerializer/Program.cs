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

// Test 1: Serialize a string directly with session from pool
{
    var testString = "c35a081a-b977-4bc6-8e7d-94a57f15f962";
    var writer = new ArrayBufferWriter<byte>();
    
    using var session = sessionPool.GetSession();
    serializer.Serialize(testString, writer, session);
    var bytes = writer.WrittenMemory.ToArray();
    
    logger.LogInformation("Test 1 - Direct string serialization with pooled session:");
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
    var arguments = new object[] { "c35a081a-b977-4bc6-8e7d-94a57f15f962" };
    
    var typeCodec = provider.GetRequiredService<TypeCodec>();
    var wellKnownTypes = provider.GetRequiredService<WellKnownTypeCollection>();
    var codecProvider = provider.GetRequiredService<CodecProvider>();
    
    using var freshSession = new SerializerSession(typeCodec, wellKnownTypes, codecProvider);
    
    var writer = new ArrayBufferWriter<byte>();
    serializer.Serialize(arguments, writer, freshSession);
    var bytes = writer.WrittenMemory.ToArray();
    
    logger.LogInformation("\nTest 3 - Object array serialization with FRESH session:");
    logger.LogInformation("  Input: object[] {{ \"{Input}\" }}", arguments[0]);
    logger.LogInformation("  Serialized to {Length} bytes: {Hex}", bytes.Length, Convert.ToHexString(bytes));
    
    // Deserialize with fresh session (will it fail to resolve references?)
    using var deserSession = new SerializerSession(typeCodec, wellKnownTypes, codecProvider);
    var memory = new ReadOnlyMemory<byte>(bytes);
    try
    {
        var deserialized = serializer.Deserialize<object[]>(memory, deserSession);
        logger.LogInformation("  Deserialized: {Count} items", deserialized?.Length ?? 0);
        if (deserialized?.Length > 0)
        {
            logger.LogInformation("  Item[0]: Type={Type}, Value={Value}", 
                deserialized[0]?.GetType()?.Name ?? "null", 
                deserialized[0]?.ToString() ?? "null");
        }
    }
    catch (Exception ex)
    {
        logger.LogError("  Failed to deserialize: {Error}", ex.Message);
    }
}

// Test 4: Check if we're getting references or values
{
    var testString = "c35a081a-b977-4bc6-8e7d-94a57f15f962";
    
    var typeCodec = provider.GetRequiredService<TypeCodec>();
    var wellKnownTypes = provider.GetRequiredService<WellKnownTypeCollection>();
    var codecProvider = provider.GetRequiredService<CodecProvider>();
    
    // Serialize twice with same session
    using var session = new SerializerSession(typeCodec, wellKnownTypes, codecProvider);
    
    var writer1 = new ArrayBufferWriter<byte>();
    serializer.Serialize(testString, writer1, session);
    var bytes1 = writer1.WrittenMemory.ToArray();
    
    var writer2 = new ArrayBufferWriter<byte>();
    serializer.Serialize(testString, writer2, session);
    var bytes2 = writer2.WrittenMemory.ToArray();
    
    logger.LogInformation("\nTest 4 - Same string serialized twice with SAME session:");
    logger.LogInformation("  First:  {Length} bytes: {Hex}", bytes1.Length, Convert.ToHexString(bytes1));
    logger.LogInformation("  Second: {Length} bytes: {Hex}", bytes2.Length, Convert.ToHexString(bytes2));
    logger.LogInformation("  Second is reference: {IsRef}", bytes2.Length < bytes1.Length);
}

// Test 5: Understand the 7-byte pattern
{
    var testArgs = new object[] { "c35a081a-b977-4bc6-8e7d-94a57f15f962" };
    
    // Use pooled session (which might have references)
    using var session = sessionPool.GetSession();
    
    var writer = new ArrayBufferWriter<byte>();
    serializer.Serialize(testArgs, writer, session);
    var bytes = writer.WrittenMemory.ToArray();
    
    logger.LogInformation("\nTest 5 - Object array with POOLED session (might have refs):");
    logger.LogInformation("  Serialized to {Length} bytes: {Hex}", bytes.Length, Convert.ToHexString(bytes));
    
    // Analyze the bytes
    if (bytes.Length > 0)
    {
        logger.LogInformation("  First byte: 0x{Byte:X2} (marker)", bytes[0]);
        if (bytes.Length > 6 && bytes.Length < 50)
        {
            logger.LogInformation("  Looks like a reference pattern!");
        }
    }
}