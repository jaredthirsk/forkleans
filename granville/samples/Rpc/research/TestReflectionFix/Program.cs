using System;
using System.Reflection;
using Orleans.Serialization.Invocation;

// Simulate an Orleans-generated IInvokable
public class TestInvokable : IInvokable
{
    // Orleans generates fields like this
    public string arg0;
    
    // But GetArgument returns null (Orleans bug)
    public object GetArgument(int index) => null;
    
    public int GetArgumentCount() => 1;
    public string GetMethodName() => "ConnectPlayer";
    public string GetInterfaceName() => "IGameRpcGrain";
    public string GetActivityName() => "IGameRpcGrain.ConnectPlayer";
    public MethodInfo GetMethod() => null;
    public Type GetInterfaceType() => null;
    public TimeSpan? GetDefaultResponseTimeout() => null;
    public object GetTarget() => null;
    public void SetTarget(ITargetHolder holder) { }
    public void SetArgument(int index, object value) { if (index == 0) arg0 = (string)value; }
    public ValueTask<Response> Invoke() => throw new NotImplementedException();
    public void Dispose() { }
}

class TestReflectionFix
{
    static void Main()
    {
        Console.WriteLine("Testing Orleans IInvokable reflection fix...");
        
        var invokable = new TestInvokable();
        invokable.arg0 = "test-player-id-12345";
        
        Console.WriteLine($"Field arg0 value: {invokable.arg0}");
        Console.WriteLine($"GetArgument(0) returns: {invokable.GetArgument(0) ?? "null"}");
        
        // Test our reflection fix
        var arguments = GetMethodArguments(invokable);
        Console.WriteLine($"After reflection fix, arguments[0] = {arguments[0]}");
        
        if (arguments[0]?.ToString() == "test-player-id-12345")
        {
            Console.WriteLine("SUCCESS: Reflection fix is working!");
        }
        else
        {
            Console.WriteLine("FAILURE: Reflection fix did not work");
        }
    }
    
    // This is our fix from RpcGrainReferenceRuntime
    static object[] GetMethodArguments(IInvokable invokable)
    {
        var argumentCount = invokable.GetArgumentCount();
        var arguments = new object[argumentCount];
        
        for (int i = 0; i < argumentCount; i++)
        {
            arguments[i] = invokable.GetArgument(i);
            
            // If GetArgument returns null, try to get the value via reflection
            if (arguments[i] == null && argumentCount > 0)
            {
                var fieldName = $"arg{i}";
                var field = invokable.GetType().GetField(fieldName, 
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                
                if (field != null)
                {
                    arguments[i] = field.GetValue(invokable);
                    Console.WriteLine($"Used reflection for arg[{i}], field {fieldName} = {arguments[i]}");
                }
            }
        }
        
        return arguments;
    }
}