namespace Shooter.Client.Services;

/// <summary>
/// Provides configuration for which game client implementation to use
/// </summary>
public interface IGameClientProvider
{
    bool UseRpc { get; }
}

public class GameClientProvider : IGameClientProvider
{
    public bool UseRpc { get; }
    
    public GameClientProvider(bool useRpc)
    {
        UseRpc = useRpc;
    }
}