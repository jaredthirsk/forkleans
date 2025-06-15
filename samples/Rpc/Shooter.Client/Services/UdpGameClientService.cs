using LiteNetLib;
using LiteNetLib.Utils;
using Microsoft.Extensions.Logging;
using Orleans.Serialization;
using Shooter.Shared.Models;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Shooter.Client.Services;

public class UdpGameClientService : IDisposable, INetEventListener
{
    private readonly ILogger<UdpGameClientService> _logger;
    private readonly HttpClient _httpClient;
    private readonly Serializer _serializer;
    private readonly IConfiguration _configuration;
    
    private NetManager? _netManager;
    private NetPeer? _serverPeer;
    private readonly NetDataWriter _writer = new();
    private readonly ConcurrentQueue<WorldState> _worldStateQueue = new();
    private TaskCompletionSource<bool>? _connectionTcs;
    private Timer? _heartbeatTimer;
    
    public event Action<WorldState>? WorldStateUpdated;
    public event Action<string>? ServerChanged;
    public event Action<List<GridSquare>>? AvailableZonesUpdated;
    
    public bool IsConnected => _serverPeer?.ConnectionState == ConnectionState.Connected;
    public string? PlayerId { get; private set; }
    public string? CurrentServerId { get; private set; }
    
    public UdpGameClientService(
        ILogger<UdpGameClientService> logger,
        HttpClient httpClient,
        Serializer serializer,
        IConfiguration configuration)
    {
        _logger = logger;
        _httpClient = httpClient;
        _serializer = serializer;
        _configuration = configuration;
    }
    
    public async Task<bool> ConnectAsync(string playerName)
    {
        try
        {
            // First register with HTTP to get server info
            var registrationResponse = await RegisterWithHttpAsync(playerName);
            if (registrationResponse == null) return false;
            
            PlayerId = registrationResponse.PlayerInfo.PlayerId;
            CurrentServerId = registrationResponse.ActionServer?.ServerId ?? "Unknown";
            
            if (registrationResponse.ActionServer == null)
            {
                _logger.LogError("No action server assigned");
                return false;
            }
            
            // Use the IP address and UDP port from the action server
            var serverHost = registrationResponse.ActionServer.IpAddress;
            var udpPort = registrationResponse.ActionServer.UdpPort;
            
            // If the server registered with localhost, extract the hostname from the HTTP endpoint
            if (serverHost == "127.0.0.1" || serverHost == "localhost")
            {
                try
                {
                    var httpUri = new Uri(registrationResponse.ActionServer.HttpEndpoint);
                    serverHost = httpUri.Host;
                    _logger.LogInformation("Using hostname from HTTP endpoint: {Host}", serverHost);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to extract hostname from HTTP endpoint, using registered IP");
                }
            }
            
            _logger.LogInformation("Connecting to UDP server at {Host}:{Port}", serverHost, udpPort);
            
            // Initialize NetManager
            _netManager = new NetManager(this)
            {
                UnconnectedMessagesEnabled = true
            };
            _netManager.Start();
            
            // Start network update loop
            _ = Task.Run(async () =>
            {
                while (_netManager != null && IsConnected)
                {
                    _netManager.PollEvents();
                    await Task.Delay(10);
                }
            });
            
            // Create connection task
            _connectionTcs = new TaskCompletionSource<bool>();
            
            // Connect to server
            _logger.LogInformation("Attempting UDP connection to {Host}:{Port}", serverHost, udpPort);
            _serverPeer = _netManager.Connect(serverHost, udpPort, "SHOOTER_CONNECT");
            
            // Wait for connection with timeout
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10));
            var connectionTask = _connectionTcs.Task;
            
            var completedTask = await Task.WhenAny(connectionTask, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                _logger.LogError("UDP connection timeout");
                Disconnect();
                return false;
            }
            
            var connected = await connectionTask;
            if (!connected)
            {
                _logger.LogError("UDP connection failed");
                return false;
            }
            
            // Send connect message
            _writer.Reset();
            _writer.Put((byte)MessageType.Connect);
            _writer.Put(PlayerId);
            _serverPeer.Send(_writer, DeliveryMethod.ReliableOrdered);
            
            // Start heartbeat timer
            _heartbeatTimer = new Timer(SendHeartbeat, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
            
            // Start processing world states
            _ = ProcessWorldStateQueue();
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to game server");
            return false;
        }
    }
    
    private async Task<UdpPlayerRegistrationResponse?> RegisterWithHttpAsync(string playerName)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("api/world/players/register", new { PlayerId = Guid.NewGuid().ToString(), Name = playerName });
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<UdpPlayerRegistrationResponse>();
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
        Disconnect();
        
        // Notify server via HTTP as well
        if (!string.IsNullOrEmpty(PlayerId))
        {
            try
            {
                await _httpClient.DeleteAsync($"api/world/disconnect-player/{PlayerId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error notifying server of disconnect");
            }
        }
    }
    
    private void Disconnect()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
        
        _serverPeer?.Disconnect();
        _serverPeer = null;
        
        _netManager?.Stop();
        _netManager = null;
        
        PlayerId = null;
        CurrentServerId = null;
    }
    
    public async Task SendPlayerInput(Vector2 moveDirection, bool isShooting)
    {
        if (_serverPeer == null || !IsConnected)
        {
            _logger.LogWarning("Cannot send input - not connected");
            return;
        }
        
        _writer.Reset();
        _writer.Put((byte)MessageType.PlayerInput);
        _writer.Put(moveDirection.X);
        _writer.Put(moveDirection.Y);
        _writer.Put(isShooting);
        
        _serverPeer.Send(_writer, DeliveryMethod.Unreliable);
        
        await Task.CompletedTask;
    }
    
    private void SendHeartbeat(object? state)
    {
        if (_serverPeer == null || !IsConnected) return;
        
        _writer.Reset();
        _writer.Put((byte)MessageType.Heartbeat);
        _serverPeer.Send(_writer, DeliveryMethod.ReliableOrdered);
    }
    
    private async Task ProcessWorldStateQueue()
    {
        while (IsConnected)
        {
            try
            {
                if (_worldStateQueue.TryDequeue(out var worldState))
                {
                    WorldStateUpdated?.Invoke(worldState);
                }
                
                await Task.Delay(16); // ~60 FPS
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing world state");
            }
        }
    }
    
    public void OnPeerConnected(NetPeer peer)
    {
        _logger.LogInformation("Connected to server");
        _connectionTcs?.TrySetResult(true);
    }
    
    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        _logger.LogInformation("Disconnected from server: {Reason}", disconnectInfo.Reason);
        _connectionTcs?.TrySetResult(false);
        Disconnect();
    }
    
    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        try
        {
            var messageType = (MessageType)reader.GetByte();
            
            switch (messageType)
            {
                case MessageType.ConnectSuccess:
                    HandleConnectSuccess(reader);
                    break;
                    
                case MessageType.ConnectFailed:
                    HandleConnectFailed(reader);
                    break;
                    
                case MessageType.WorldStateUpdate:
                    HandleWorldStateUpdate(reader);
                    break;
                    
                case MessageType.ServerInfo:
                    HandleServerInfo(reader);
                    break;
                    
                default:
                    _logger.LogWarning("Unknown message type: {MessageType}", messageType);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling network message");
        }
        finally
        {
            reader.Recycle();
        }
    }
    
    private void HandleConnectSuccess(NetPacketReader reader)
    {
        var playerId = reader.GetString();
        _logger.LogInformation("Connected successfully with player ID: {PlayerId}", playerId);
    }
    
    private void HandleConnectFailed(NetPacketReader reader)
    {
        var reason = reader.GetString();
        _logger.LogError("Connection failed: {Reason}", reason);
        _connectionTcs?.TrySetResult(false);
    }
    
    private void HandleWorldStateUpdate(NetPacketReader reader)
    {
        try
        {
            var bytes = reader.GetBytesWithLength();
            var worldState = _serializer.Deserialize<WorldState>(bytes);
            
            if (worldState != null)
            {
                _worldStateQueue.Enqueue(worldState);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deserializing world state");
        }
    }
    
    private void HandleServerInfo(NetPacketReader reader)
    {
        var serverId = reader.GetString();
        CurrentServerId = serverId;
        ServerChanged?.Invoke(serverId);
    }
    
    public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
    {
        _logger.LogError("Network error: {Error}", socketError);
    }
    
    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
    {
        // Not used by client
    }
    
    public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
    {
        // Could display latency in UI if desired
    }
    
    public void OnConnectionRequest(ConnectionRequest request)
    {
        // Client doesn't receive connection requests
    }
    
    public void Dispose()
    {
        Disconnect();
    }
}


// Response types
internal class UdpPlayerRegistrationResponse
{
    public required PlayerInfo PlayerInfo { get; init; }
    public ActionServerInfo? ActionServer { get; init; }
}