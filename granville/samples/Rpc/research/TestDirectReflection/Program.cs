using System;
using System.Reflection;

// Simulate what Orleans generates
public interface IInvokable
{
    int GetArgumentCount();
    object GetArgument(int index);
}

public abstract class RequestBase : IInvokable
{
    public abstract int GetArgumentCount();
    public virtual object GetArgument(int index)
    {
        throw new NotSupportedException("GetArgument is not supported on this type");
    }
}

// This is what Orleans code generator creates
public class GeneratedInvokable : RequestBase
{
    public string arg0;
    
    public override int GetArgumentCount() => 1;
    
    // Orleans generates this but it never gets called due to base class
    public new object GetArgument(int index)
    {
        switch (index)
        {
            case 0: return arg0;
            default: throw new ArgumentOutOfRangeException();
        }
    }
}

class Program
{
    static void Main()
    {
        Console.WriteLine("Testing Orleans proxy argument extraction...");
        
        var invokable = new GeneratedInvokable { arg0 = "test-player-123" };
        IInvokable iinvokable = invokable;
        
        Console.WriteLine($"Invokable type: {invokable.GetType().FullName}");
        Console.WriteLine($"Field arg0 value: {invokable.arg0}");
        Console.WriteLine($"Argument count: {iinvokable.GetArgumentCount()}");
        
        // Try GetArgument through interface
        try
        {
            var value = iinvokable.GetArgument(0);
            Console.WriteLine($"GetArgument(0) returned: {value}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GetArgument(0) threw: {ex.GetType().Name} - {ex.Message}");
            
            // Use reflection workaround
            var invokableType = invokable.GetType();
            var field = invokableType.GetField("arg0", 
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            
            if (field != null)
            {
                var reflectionValue = field.GetValue(invokable);
                Console.WriteLine($"✅ Reflection workaround SUCCESS: Field value = {reflectionValue}");
            }
            else
            {
                Console.WriteLine("❌ Reflection workaround FAILED: Field not found");
            }
        }
    }
}