using Forkleans;
using Forkleans.Rpc;
using Forkleans.Rpc.Transport.LiteNetLib;
using Shooter.Shared.Models;
using Shooter.Shared.RpcInterfaces;
using System.Net.Http.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Shooter.Client.Services;

/// <summary>
/// Game client service that uses Forkleans RPC for all communication with ActionServers.
/// This provides a single LiteNetLib UDP connection for all game operations.
/// </summary>
public class ForleansRpcGameClientService : IDisposable
{
    private readonly ILogger<ForleansRpcGameClientService> _logger;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private Forkleans.IClusterClient? _rpcClient;
    private IGameRpcGrain? _gameGrain;
    private Timer? _worldStateTimer;
    private Timer? _heartbeatTimer;
    private CancellationTokenSource? _cancellationTokenSource;
    
    public event Action<WorldState>? WorldStateUpdated;
    public event Action<string>? ServerChanged;
    public event Action<List<GridSquare>>? AvailableZonesUpdated;
    
    public bool IsConnected { get; private set; }
    public string? PlayerId { get; private set; }
    public string? CurrentServerId { get; private set; }
    
    public ForleansRpcGameClientService(
        ILogger<ForleansRpcGameClientService> logger,
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _logger = logger;
        _httpClient = httpClient;
        _configuration = configuration;
    }
    
    public async Task<bool> ConnectAsync(string playerName)
    {
        try
        {
            _cancellationTokenSource = new CancellationTokenSource();
            
            // First register with HTTP to get server info and player ID
            var registrationResponse = await RegisterWithHttpAsync(playerName);
            if (registrationResponse == null) return false;
            
            PlayerId = registrationResponse.PlayerInfo?.PlayerId ?? string.Empty;
            CurrentServerId = registrationResponse.ActionServer?.ServerId ?? "Unknown";
            
            if (registrationResponse.ActionServer == null)
            {
                _logger.LogError("No action server assigned");
                return false;
            }
            
            // Extract host and RPC port from ActionServer info
            var serverHost = registrationResponse.ActionServer.IpAddress;
            var rpcPort = registrationResponse.ActionServer.RpcPort;
            
            if (rpcPort == 0)
            {
                _logger.LogError("ActionServer did not report RPC port");
                return false;
            }
            
            _logger.LogInformation("Connecting to Forkleans RPC server at {Host}:{Port}", serverHost, rpcPort);
            
            // Create RPC client
            var hostBuilder = Host.CreateDefaultBuilder()
                .UseOrleansRpcClient(rpcBuilder =>
                {
                    rpcBuilder.ConnectTo(serverHost, rpcPort);
                    rpcBuilder.UseLiteNetLib();
                })
                .Build();
                
            await hostBuilder.StartAsync();
            
            _rpcClient = hostBuilder.Services.GetRequiredService<Forkleans.IClusterClient>();
            
            // Get the game grain for this server
            _gameGrain = _rpcClient.GetGrain<IGameRpcGrain>(CurrentServerId);
            
            // Connect via RPC
            var connected = await _gameGrain.ConnectPlayer(PlayerId);
            if (!connected)
            {
                _logger.LogError("Failed to connect player via RPC");
                return false;
            }
            
            IsConnected = true;
            
            // Start polling for world state
            _worldStateTimer = new Timer(async _ => await PollWorldState(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(16));
            
            // Start heartbeat
            _heartbeatTimer = new Timer(async _ => await SendHeartbeat(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
            
            _logger.LogInformation("Connected to game via Forkleans RPC");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to game server");
            return false;
        }
    }
    
    private async Task<PlayerRegistrationResponse?> RegisterWithHttpAsync(string playerName)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/world/players/register", 
                new { PlayerId = Guid.NewGuid().ToString(), Name = playerName });
            
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<Shooter.Client.Services.PlayerRegistrationResponse>();
            }
            
            _logger.LogError("Failed to register player: {StatusCode}", response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering player");
            return null;
        }
    }
    
    public async Task DisconnectAsync()
    {
        if (_gameGrain != null && IsConnected)
        {
            try
            {
                await _gameGrain.DisconnectPlayer(PlayerId!);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting player");
            }
        }
        
        Cleanup();
    }
    
    public async Task SendPlayerInput(Vector2 moveDirection, bool isShooting)
    {
        if (_gameGrain == null || !IsConnected || string.IsNullOrEmpty(PlayerId))
        {
            return;
        }
        
        try
        {
            await _gameGrain.UpdatePlayerInput(PlayerId, moveDirection, isShooting);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send player input");
        }
    }
    
    private async Task PollWorldState()
    {
        if (_gameGrain == null || !IsConnected || _cancellationTokenSource?.Token.IsCancellationRequested == true)
        {
            return;
        }
        
        try
        {
            var worldState = await _gameGrain.GetWorldState();
            if (worldState != null)
            {
                WorldStateUpdated?.Invoke(worldState);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get world state");
        }
    }
    
    private async Task SendHeartbeat()
    {
        if (_gameGrain == null || !IsConnected || _cancellationTokenSource?.Token.IsCancellationRequested == true)
        {
            return;
        }
        
        try
        {
            // The UpdatePlayerInput with zero movement acts as a heartbeat
            await _gameGrain.UpdatePlayerInput(PlayerId!, Vector2.Zero, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send heartbeat");
        }
    }
    
    private void Cleanup()
    {
        IsConnected = false;
        
        _cancellationTokenSource?.Cancel();
        _worldStateTimer?.Dispose();
        _heartbeatTimer?.Dispose();
        
        _gameGrain = null;
        _rpcClient = null;
        
        PlayerId = null;
        CurrentServerId = null;
    }
    
    public void Dispose()
    {
        Cleanup();
    }
}

