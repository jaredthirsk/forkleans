using Orleans;

public interface ITestGrain : IGrainWithIntegerKey
{
    Task<string> SayHello();
}

public class TestGrain : Grain, ITestGrain
{
    public Task<string> SayHello() => Task.FromResult("Hello");
}