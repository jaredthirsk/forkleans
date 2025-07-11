using Orleans;

namespace TestCodeGen;

[GenerateSerializer]
public class TestState
{
    [Id(0)]
    public string Name { get; set; } = "";
}

public interface ITestGrain : IGrainWithIntegerKey
{
    Task<string> GetName();
}

public class TestGrain : Grain, ITestGrain
{
    private TestState state = new();
    
    public Task<string> GetName()
    {
        return Task.FromResult(state.Name);
    }
}

class Program
{
    static void Main()
    {
        Console.WriteLine("Test Code Generation");
    }
}