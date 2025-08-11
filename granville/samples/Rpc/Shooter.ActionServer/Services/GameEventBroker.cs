using Microsoft.Extensions.Logging;
using Shooter.Shared.Models;
using System.Collections.Concurrent;

namespace Shooter.ActionServer.Services;

/// <summary>
/// Broker service that connects WorldSimulation events to RPC grain notifications.
/// This solves the issue where GameRpcGrain can't directly subscribe to WorldSimulation
/// because it's created dynamically by the RPC framework.
/// </summary>
public class GameEventBroker
{
    private readonly ILogger<GameEventBroker> _logger;
    private readonly ConcurrentBag<Action<GameOverMessage>> _gameOverHandlers = new();
    private readonly ConcurrentBag<Action<VictoryPauseMessage>> _victoryPauseHandlers = new();
    private readonly ConcurrentBag<Action> _gameRestartHandlers = new();
    private readonly ConcurrentBag<Action<ChatMessage>> _chatMessageHandlers = new();

    public GameEventBroker(ILogger<GameEventBroker> logger)
    {
        _logger = logger;
    }

    // Events that WorldSimulation can raise
    public void RaiseGameOver(GameOverMessage message)
    {
        _logger.LogInformation("GameEventBroker: Raising game over event to {HandlerCount} handlers", _gameOverHandlers.Count);
        foreach (var handler in _gameOverHandlers)
        {
            try
            {
                handler(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in game over handler");
            }
        }
    }

    public void RaiseVictoryPause(VictoryPauseMessage message)
    {
        _logger.LogInformation("GameEventBroker: Raising victory pause event to {HandlerCount} handlers", _victoryPauseHandlers.Count);
        foreach (var handler in _victoryPauseHandlers)
        {
            try
            {
                handler(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in victory pause handler");
            }
        }
    }

    public void RaiseGameRestart()
    {
        _logger.LogInformation("GameEventBroker: Raising game restart event to {HandlerCount} handlers", _gameRestartHandlers.Count);
        foreach (var handler in _gameRestartHandlers)
        {
            try
            {
                handler();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in game restart handler");
            }
        }
    }

    // Methods for GameRpcGrain to subscribe
    public void SubscribeToGameOver(Action<GameOverMessage> handler)
    {
        _gameOverHandlers.Add(handler);
        _logger.LogInformation("GameEventBroker: Game over handler subscribed, total handlers: {Count}", _gameOverHandlers.Count);
    }

    public void SubscribeToVictoryPause(Action<VictoryPauseMessage> handler)
    {
        _victoryPauseHandlers.Add(handler);
        _logger.LogInformation("GameEventBroker: Victory pause handler subscribed, total handlers: {Count}", _victoryPauseHandlers.Count);
    }

    public void SubscribeToGameRestart(Action handler)
    {
        _gameRestartHandlers.Add(handler);
        _logger.LogInformation("GameEventBroker: Game restart handler subscribed, total handlers: {Count}", _gameRestartHandlers.Count);
    }
    
    // Chat message handling
    public void RaiseChatMessage(ChatMessage message)
    {
        _logger.LogInformation("GameEventBroker: Raising chat message from {Sender} to {HandlerCount} handlers", 
            message.SenderName, _chatMessageHandlers.Count);
        foreach (var handler in _chatMessageHandlers)
        {
            try
            {
                handler(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in chat message handler");
            }
        }
    }
    
    public void SubscribeToChatMessage(Action<ChatMessage> handler)
    {
        _chatMessageHandlers.Add(handler);
        _logger.LogInformation("GameEventBroker: Chat message handler subscribed, total handlers: {Count}", _chatMessageHandlers.Count);
    }
}