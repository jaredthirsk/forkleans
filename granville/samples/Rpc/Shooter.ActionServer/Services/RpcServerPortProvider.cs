namespace Shooter.ActionServer.Services;

/// <summary>
/// Service to provide RPC port to other services
/// </summary>
public class RpcServerPortProvider
{
    private readonly TaskCompletionSource<int> _portTcs = new();
    
    public void SetPort(int port)
    {
        _portTcs.TrySetResult(port);
    }
    
    public async Task<int> WaitForPortAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        
        try
        {
            return await _portTcs.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("RPC port was not set within the timeout period");
        }
    }
    
    public int Port => _portTcs.Task.IsCompletedSuccessfully ? _portTcs.Task.Result : 0;
}