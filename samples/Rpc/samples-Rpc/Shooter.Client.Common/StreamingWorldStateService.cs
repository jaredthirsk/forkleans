using Microsoft.Extensions.Logging;
using Shooter.Shared.Models;
using Shooter.Shared.RpcInterfaces;

namespace Shooter.Client.Common;

/// <summary>
/// Service that uses IAsyncEnumerable to stream world state updates.
/// </summary>
public class StreamingWorldStateService
{
    private readonly ILogger<StreamingWorldStateService> _logger;
    private readonly IGameRpcGrain _gameGrain;
    private CancellationTokenSource? _streamCancellation;
    
    public event Action<WorldState>? WorldStateUpdated;
    public event Action<ZoneStatistics>? ZoneStatsUpdated;
    public event Action<Dictionary<string, List<EntityState>>>? AdjacentEntitiesUpdated;
    
    public StreamingWorldStateService(ILogger<StreamingWorldStateService> logger, IGameRpcGrain gameGrain)
    {
        _logger = logger;
        _gameGrain = gameGrain;
    }
    
    public Task StartStreamingAsync(string playerId)
    {
        _streamCancellation = new CancellationTokenSource();
        var token = _streamCancellation.Token;
        
        // Start streaming tasks
        var worldStateTask = StreamWorldStateAsync(token);
        var zoneStatsTask = StreamZoneStatsAsync(token);
        var adjacentEntitiesTask = StreamAdjacentEntitiesAsync(playerId, token);
        
        // Fire and forget - these will run until cancelled
        _ = Task.WhenAll(worldStateTask, zoneStatsTask, adjacentEntitiesTask)
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    _logger.LogError(t.Exception, "Streaming tasks failed");
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
        
        return Task.CompletedTask;
    }
    
    public void StopStreaming()
    {
        _streamCancellation?.Cancel();
        _streamCancellation?.Dispose();
        _streamCancellation = null;
    }
    
    private async Task StreamWorldStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var worldState in _gameGrain.StreamWorldStateUpdates().WithCancellation(cancellationToken))
            {
                WorldStateUpdated?.Invoke(worldState);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("World state streaming cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in world state stream");
        }
    }
    
    private async Task StreamZoneStatsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var stats in _gameGrain.StreamZoneStatistics().WithCancellation(cancellationToken))
            {
                ZoneStatsUpdated?.Invoke(stats);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Zone stats streaming cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in zone stats stream");
        }
    }
    
    private async Task StreamAdjacentEntitiesAsync(string playerId, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var adjacentEntities in _gameGrain.StreamAdjacentZoneEntities(playerId).WithCancellation(cancellationToken))
            {
                AdjacentEntitiesUpdated?.Invoke(adjacentEntities.EntitiesByZone);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Adjacent entities streaming cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in adjacent entities stream");
        }
    }
}