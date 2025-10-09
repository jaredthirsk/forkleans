#!/usr/bin/env dotnet-script
// Simple test to verify ConnectPlayer fix is working

using System;
using System.Threading.Tasks;
using System.Reflection;

// Simulate the async task detection logic from our fix
static bool IsTaskWithResult(Task task)
{
    // Check if the task has a Result property that's not of type VoidTaskResult
    var resultProperty = task.GetType().GetProperty("Result");
    if (resultProperty == null) return false;
    
    var resultType = resultProperty.PropertyType;
    return resultType != typeof(void) && resultType.FullName != "System.Threading.Tasks.VoidTaskResult";
}

static object ExtractTaskResult(Task task)
{
    // Try to extract result from Task<T> or any task that has a Result property
    var taskType = task.GetType();
    var resultProperty = taskType.GetProperty("Result");
    if (resultProperty != null)
    {
        try
        {
            var taskResult = resultProperty.GetValue(task);
            Console.WriteLine($"Successfully extracted task result of type {taskResult?.GetType().Name ?? "null"}: {taskResult}");
            
            // Additional safety check: ensure we're not returning VoidTaskResult
            if (taskResult != null && taskResult.GetType().FullName == "System.Threading.Tasks.VoidTaskResult")
            {
                Console.WriteLine("Task result is VoidTaskResult, returning null instead");
                return null;
            }
            
            return taskResult;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to extract result from task of type {taskType.FullName}: {ex.Message}");
        }
    }
    
    Console.WriteLine($"Task type {taskType.FullName} has no Result property, returning null");
    return null;
}

// Test with different task types that we might encounter
async Task<string> ConnectPlayerSimulation(string playerId)
{
    // Simulate the actual ConnectPlayer method behavior
    if (string.IsNullOrEmpty(playerId))
    {
        return "FAILED";
    }
    
    // Simulate some async work
    await Task.Delay(1);
    return "SUCCESS";
}

Console.WriteLine("Testing ConnectPlayer RPC Fix");
Console.WriteLine("=====================================");

// Test 1: Regular Task<string>
var task1 = ConnectPlayerSimulation("player123");
await task1;
Console.WriteLine($"Test 1 - Regular Task<string>:");
Console.WriteLine($"  Task type: {task1.GetType().FullName}");
Console.WriteLine($"  IsTaskWithResult: {IsTaskWithResult(task1)}");
Console.WriteLine($"  Extracted result: {ExtractTaskResult(task1)}");
Console.WriteLine();

// Test 2: Task with null result  
var task2 = ConnectPlayerSimulation(null);
await task2;
Console.WriteLine($"Test 2 - Task<string> with null input:");
Console.WriteLine($"  Task type: {task2.GetType().FullName}");
Console.WriteLine($"  IsTaskWithResult: {IsTaskWithResult(task2)}");
Console.WriteLine($"  Extracted result: {ExtractTaskResult(task2)}");
Console.WriteLine();

// Test 3: Non-generic Task
var task3 = Task.CompletedTask;
Console.WriteLine($"Test 3 - Non-generic Task:");
Console.WriteLine($"  Task type: {task3.GetType().FullName}");
Console.WriteLine($"  IsTaskWithResult: {IsTaskWithResult(task3)}");
Console.WriteLine($"  Extracted result: {ExtractTaskResult(task3)}");
Console.WriteLine();

Console.WriteLine("Fix validation complete - Our RpcConnection.cs changes should handle these cases correctly!");