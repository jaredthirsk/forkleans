using System;
using System.Buffers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Serialization;
using Orleans.Serialization.Session;
using Orleans.Serialization.Serializers;
using Orleans.Serialization.TypeSystem;
using Granville.Rpc;

/// <summary>
/// Simple test to verify that isolated session serialization works correctly
/// and prevents reference-based serialization issues across independent runtimes.
/// </summary>
class SessionIsolationTest
{
    static void Main(string[] args)
    {
        Console.WriteLine("=== Testing RPC Session Isolation ===");
        
        // Setup Orleans serialization infrastructure
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        services.AddSerializer();
        
        var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetRequiredService<Serializer>();
        var typeCodec = serviceProvider.GetRequiredService<TypeCodec>();
        var wellKnownTypes = serviceProvider.GetRequiredService<WellKnownTypeCollection>();
        var codecProvider = serviceProvider.GetRequiredService<CodecProvider>();
        var logger = serviceProvider.GetRequiredService<ILogger<RpcSerializationSessionFactory>>();
        
        // Create session factory
        var sessionFactory = new RpcSerializationSessionFactory(typeCodec, wellKnownTypes, codecProvider, logger);
        
        Console.WriteLine("\n1. Testing normal Orleans serialization (shared session):");
        TestNormalSerialization(serializer);
        
        Console.WriteLine("\n2. Testing isolated session serialization:");
        TestIsolatedSessionSerialization(serializer, sessionFactory);
        
        Console.WriteLine("\n3. Testing cross-session deserialization:");
        TestCrossSessionDeserialization(serializer, sessionFactory);
        
        Console.WriteLine("\n=== Test Complete ===");
    }
    
    static void TestNormalSerialization(Serializer serializer)
    {
        var testString = "7e3dc25c-e7d5-485d-9384-632cd29ad0e0";
        var args = new object[] { testString };
        
        // Normal serialization (may use references)
        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(args, writer);
        var serializedBytes = writer.WrittenMemory.ToArray();
        
        Console.WriteLine($"  Serialized {testString} to {serializedBytes.Length} bytes: {Convert.ToHexString(serializedBytes)}");
        
        // Deserialize in same context
        var deserialized = serializer.Deserialize<object[]>(serializedBytes);
        Console.WriteLine($"  Deserialized: {deserialized[0]} (Success: {testString == deserialized[0]?.ToString()})");
    }
    
    static void TestIsolatedSessionSerialization(Serializer serializer, RpcSerializationSessionFactory sessionFactory)
    {
        var testString = "7e3dc25c-e7d5-485d-9384-632cd29ad0e0";
        var args = new object[] { testString };
        
        // Isolated session serialization
        var serializedBytes = sessionFactory.SerializeArgumentsWithIsolatedSession(serializer, args);
        
        Console.WriteLine($"  Isolated serialized {testString} to {serializedBytes.Length} bytes: {Convert.ToHexString(serializedBytes)}");
        
        // Deserialize with fresh isolated session
        var deserialized = sessionFactory.DeserializeWithIsolatedSession<object[]>(serializer, serializedBytes);
        Console.WriteLine($"  Isolated deserialized: {deserialized[0]} (Success: {testString == deserialized[0]?.ToString()})");
    }
    
    static void TestCrossSessionDeserialization(Serializer serializer, RpcSerializationSessionFactory sessionFactory)
    {
        var testString = "7e3dc25c-e7d5-485d-9384-632cd29ad0e0";
        var args = new object[] { testString };
        
        Console.WriteLine("  Simulating client-server scenario:");
        
        // Client side: serialize with isolated session
        Console.WriteLine("  [CLIENT] Serializing with isolated session...");
        var clientBytes = sessionFactory.SerializeArgumentsWithIsolatedSession(serializer, args);
        Console.WriteLine($"  [CLIENT] Serialized to {clientBytes.Length} bytes: {Convert.ToHexString(clientBytes)}");
        
        // Server side: deserialize with isolated session (simulating independent runtime)
        Console.WriteLine("  [SERVER] Deserializing with fresh isolated session...");
        var serverResult = sessionFactory.DeserializeWithIsolatedSession<object[]>(serializer, clientBytes);
        Console.WriteLine($"  [SERVER] Deserialized: {serverResult[0]} (Success: {testString == serverResult[0]?.ToString()})");
        
        // This should work correctly with isolated sessions
        var success = testString == serverResult[0]?.ToString();
        Console.WriteLine($"  Cross-session test result: {(success ? "PASS" : "FAIL")}");
        
        if (!success)
        {
            Console.WriteLine("  ❌ ISOLATED SESSION FIX DID NOT WORK");
        }
        else 
        {
            Console.WriteLine("  ✅ ISOLATED SESSION FIX SUCCESSFUL");
        }
    }
}