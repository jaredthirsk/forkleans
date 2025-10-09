#!/usr/bin/env dotnet-script
// Debug the AsyncStateMachineBox result property issue

using System;
using System.Threading.Tasks;
using System.Reflection;

static bool IsTaskWithResult(Task task)
{
    Console.WriteLine($"  IsTaskWithResult debugging for: {task.GetType().FullName}");
    
    // Check if the task has a Result property that's not of type VoidTaskResult
    var resultProperty = task.GetType().GetProperty("Result");
    Console.WriteLine($"  Result property: {resultProperty?.Name} (exists: {resultProperty != null})");
    
    if (resultProperty == null) 
    {
        Console.WriteLine("  No Result property found, returning false");
        return false;
    }
    
    var resultType = resultProperty.PropertyType;
    Console.WriteLine($"  Result property type: {resultType.FullName}");
    Console.WriteLine($"  Is not void: {resultType != typeof(void)}");
    Console.WriteLine($"  Is not VoidTaskResult: {resultType.FullName != "System.Threading.Tasks.VoidTaskResult"}");
    
    bool result = resultType != typeof(void) && resultType.FullName != "System.Threading.Tasks.VoidTaskResult";
    Console.WriteLine($"  IsTaskWithResult returning: {result}");
    return result;
}

async Task<string> ConnectPlayerSimulation(string playerId)
{
    if (string.IsNullOrEmpty(playerId))
        return "FAILED";
    await Task.Delay(1);
    return "SUCCESS";
}

Console.WriteLine("Debugging AsyncStateMachineBox Issue");
Console.WriteLine("===================================");

// Test the exact same scenario as the logs show
var task1 = ConnectPlayerSimulation("player123");
await task1;

Console.WriteLine("Test 1: AsyncStateMachineBox");
Console.WriteLine($"Task type: {task1.GetType().FullName}");
Console.WriteLine($"Task completed: {task1.IsCompleted}");
Console.WriteLine($"Task result: {task1.Result}");

bool hasResult = IsTaskWithResult(task1);
Console.WriteLine($"IsTaskWithResult: {hasResult}");

// Check the condition from RpcConnection.cs line 386-387
bool isNonGenericCondition = task1 is Task && 
    !(task1.GetType().IsGenericType && task1.GetType().GetGenericTypeDefinition() == typeof(Task<>)) && 
    !IsTaskWithResult(task1);

Console.WriteLine($"Non-generic condition check: {isNonGenericCondition}");
Console.WriteLine($"  is Task: {task1 is Task}");
Console.WriteLine($"  IsGenericType: {task1.GetType().IsGenericType}");
Console.WriteLine($"  GetGenericTypeDefinition == typeof(Task<>): {task1.GetType().IsGenericType && task1.GetType().GetGenericTypeDefinition() == typeof(Task<>)}");
Console.WriteLine($"  !IsTaskWithResult: {!IsTaskWithResult(task1)}");

Console.WriteLine("This explains why the condition is being met and null is returned!");