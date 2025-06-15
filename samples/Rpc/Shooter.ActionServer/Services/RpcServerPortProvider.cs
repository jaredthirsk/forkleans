namespace Shooter.ActionServer.Services;

/// <summary>
/// Provides access to the dynamically assigned RPC server port.
/// </summary>
public class RpcServerPortProvider
{
    private int _port;
    private readonly object _lock = new();
    
    public int Port 
    { 
        get 
        { 
            lock (_lock) 
            { 
                return _port; 
            } 
        } 
    }
    
    public void SetPort(int port)
    {
        lock (_lock)
        {
            _port = port;
        }
    }
    
    public async Task<int> WaitForPortAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var endTime = DateTime.UtcNow + timeout;
        
        while (DateTime.UtcNow < endTime)
        {
            lock (_lock)
            {
                if (_port > 0)
                    return _port;
            }
            
            await Task.Delay(100, cancellationToken);
        }
        
        throw new TimeoutException("RPC server port was not assigned within the timeout period");
    }
}