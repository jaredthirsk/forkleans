using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Orleans.Serialization.Invocation;

// Test program to debug GetArgument null issue

// Dummy interface to trigger code generation
[GenerateSerializer]
public interface ITestGrain : IGrainWithStringKey
{
    Task<string> TestMethod(string arg1);
}

var builder = Host.CreateApplicationBuilder(args);

// Configure logging to see what's happening
builder.Logging.SetMinimumLevel(LogLevel.Debug);
builder.Logging.AddConsole();

// Add Orleans services to get the service provider
builder.Services.AddOrleans(siloBuilder =>
{
    siloBuilder.UseLocalhostClustering()
               .Configure<ClusterOptions>(options =>
               {
                   options.ClusterId = "test-cluster";
                   options.ServiceId = "test-service";
               });
});

var host = builder.Build();

try
{
    await host.StartAsync();
    
    var logger = host.Services.GetRequiredService<ILogger<Program>>();
    var serviceProvider = host.Services;
    
    logger.LogInformation("Testing Orleans-generated invokable GetArgument behavior...");
    
    // Find an Orleans-generated invokable type
    var assemblies = AppDomain.CurrentDomain.GetAssemblies();
    Type invokableType = null;
    
    foreach (var assembly in assemblies)
    {
        try
        {
            var types = assembly.GetTypes();
            foreach (var type in types)
            {
                // Look for generated invokable types
                if (type.Name.Contains("orleans_g_") && 
                    type.Name.Contains("ConnectPlayer") &&
                    typeof(IInvokable).IsAssignableFrom(type))
                {
                    invokableType = type;
                    logger.LogInformation("Found invokable type: {TypeName} in assembly {Assembly}", 
                        type.FullName, assembly.GetName().Name);
                    break;
                }
            }
            if (invokableType != null) break;
        }
        catch (Exception ex)
        {
            // Some assemblies might not be loadable
            logger.LogTrace(ex, "Could not load types from assembly {Assembly}", assembly.FullName);
        }
    }
    
    if (invokableType == null)
    {
        logger.LogError("Could not find any Orleans-generated invokable types for ConnectPlayer");
        return;
    }
    
    // Create an instance using the service provider
    try
    {
        var invokable = (IInvokable)ActivatorUtilities.GetServiceOrCreateInstance(serviceProvider, invokableType);
        logger.LogInformation("Created invokable instance: {Type}", invokable.GetType().FullName);
        
        // List all fields
        var fields = invokableType.GetFields(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        logger.LogInformation("Invokable has {Count} fields:", fields.Length);
        foreach (var field in fields)
        {
            logger.LogInformation("  Field: {Name} ({Type})", field.Name, field.FieldType.Name);
        }
        
        // Try to set arg0 field
        var arg0Field = invokableType.GetField("arg0", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (arg0Field != null)
        {
            var testValue = "test-player-id";
            arg0Field.SetValue(invokable, testValue);
            logger.LogInformation("Set arg0 field to: {Value}", testValue);
            
            // Now test GetArgument
            try
            {
                var result = invokable.GetArgument(0);
                logger.LogInformation("GetArgument(0) returned: {Type}: {Value}", 
                    result?.GetType()?.Name ?? "null", result?.ToString() ?? "null");
                
                if (result == null)
                {
                    logger.LogError("BUG CONFIRMED: GetArgument(0) returned null despite arg0 field being set!");
                    
                    // Double-check the field value
                    var fieldValue = arg0Field.GetValue(invokable);
                    logger.LogInformation("Direct field access returns: {Type}: {Value}",
                        fieldValue?.GetType()?.Name ?? "null", fieldValue?.ToString() ?? "null");
                }
                else
                {
                    logger.LogInformation("SUCCESS: GetArgument(0) returned the expected value!");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "GetArgument(0) threw an exception");
            }
        }
        else
        {
            logger.LogError("Could not find arg0 field on invokable type");
        }
        
        // Test GetArgumentCount
        try
        {
            var count = invokable.GetArgumentCount();
            logger.LogInformation("GetArgumentCount() returned: {Count}", count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetArgumentCount() threw an exception");
        }
        
        // Test GetMethodName
        try
        {
            var methodName = invokable.GetMethodName();
            logger.LogInformation("GetMethodName() returned: {MethodName}", methodName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GetMethodName() threw an exception");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error creating or testing invokable");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Host startup failed: {ex}");
}
finally
{
    await host.StopAsync();
}