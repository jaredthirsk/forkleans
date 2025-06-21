using LiteNetLib;
using LiteNetLib.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Forkleans.Serialization;
using Shooter.ActionServer.Simulation;
using Shooter.Shared.Models;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Shooter.ActionServer.Services;

public class UdpGameServer : BackgroundService, INetEventListener
{
    private readonly ILogger<UdpGameServer> _logger;
    private readonly IWorldSimulation _worldSimulation;
    private readonly GameService _gameService;
    private readonly Serializer _serializer;
    private readonly IConfiguration _configuration;
    
    private NetManager? _netManager;
    private readonly ConcurrentDictionary<int, ConnectedPlayer> _connectedPlayers = new();
    private readonly NetDataWriter _writer = new();
    private int _udpPort;
    
    private class ConnectedPlayer
    {
        public string PlayerId { get; set; } = string.Empty;
        public NetPeer Peer { get; set; } = null!;
        public DateTime LastHeartbeat { get; set; }
    }
    
    public UdpGameServer(
        ILogger<UdpGameServer> logger,
        IWorldSimulation worldSimulation,
        GameService gameService,
        Serializer serializer,
        IConfiguration configuration)
    {
        _logger = logger;
        _worldSimulation = worldSimulation;
        _gameService = gameService;
        _serializer = serializer;
        _configuration = configuration;
    }
    
    public int UdpPort => _udpPort;
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Find an available UDP port
        _udpPort = FindAvailablePort(9000, 9100);
        
        _netManager = new NetManager(this)
        {
            UnconnectedMessagesEnabled = true,
            BroadcastReceiveEnabled = true
        };
        _netManager.Start(_udpPort);
        
        _logger.LogInformation("UDP Game Server started on port {Port}", _udpPort);
        
        // Start background tasks
        var broadcastTask = BroadcastWorldStateLoop(stoppingToken);
        var heartbeatTask = CheckHeartbeatsLoop(stoppingToken);
        var networkUpdateTask = NetworkUpdateLoop(stoppingToken);
        
        await Task.WhenAll(broadcastTask, heartbeatTask, networkUpdateTask);
        
        _netManager.Stop();
    }
    
    private async Task NetworkUpdateLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            _netManager?.PollEvents();
            await Task.Delay(10, cancellationToken); // Poll every 10ms
        }
    }
    
    private int FindAvailablePort(int startPort, int endPort)
    {
        _logger.LogInformation("Searching for available UDP port between {StartPort} and {EndPort}", startPort, endPort);
        
        for (int port = startPort; port <= endPort; port++)
        {
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Bind(new IPEndPoint(IPAddress.Any, port));
                _logger.LogInformation("Successfully bound to UDP port {Port}", port);
                return port;
            }
            catch (SocketException ex)
            {
                _logger.LogDebug("Port {Port} is in use: {Message}", port, ex.Message);
            }
        }
        throw new InvalidOperationException($"No available UDP ports found between {startPort} and {endPort}");
    }
    
    private async Task BroadcastWorldStateLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var worldState = await _gameService.GetWorldState();
                BroadcastWorldState(worldState);
                
                // Broadcast at 60Hz for smooth gameplay
                await Task.Delay(16, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting world state");
            }
        }
    }
    
    private async Task CheckHeartbeatsLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                var timeoutPlayers = _connectedPlayers
                    .Where(p => (now - p.Value.LastHeartbeat).TotalSeconds > 30)
                    .ToList();
                
                foreach (var (peerId, player) in timeoutPlayers)
                {
                    _logger.LogWarning("Player {PlayerId} timed out", player.PlayerId);
                    player.Peer.Disconnect();
                    _connectedPlayers.TryRemove(peerId, out _);
                    await _gameService.DisconnectPlayer(player.PlayerId);
                }
                
                await Task.Delay(5000, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking heartbeats");
            }
        }
    }
    
    private void BroadcastWorldState(WorldState worldState)
    {
        if (_connectedPlayers.IsEmpty) return;
        
        _writer.Reset();
        _writer.Put((byte)MessageType.WorldStateUpdate);
        
        // Serialize world state efficiently
        var bytes = _serializer.SerializeToArray(worldState);
        _writer.PutBytesWithLength(bytes);
        
        // Send to all connected players
        foreach (var player in _connectedPlayers.Values)
        {
            player.Peer.Send(_writer, DeliveryMethod.Unreliable);
        }
    }
    
    public void OnPeerConnected(NetPeer peer)
    {
        _logger.LogInformation("Peer connected from {Address}", peer.Address);
        
        // Send connection acknowledgment
        _writer.Reset();
        _writer.Put((byte)MessageType.ConnectionAccepted);
        _writer.Put(_udpPort);
        peer.Send(_writer, DeliveryMethod.ReliableOrdered);
    }
    
    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        _logger.LogInformation("Peer disconnected from {Address}: {Reason}", peer.Address, disconnectInfo.Reason);
        
        if (_connectedPlayers.TryRemove(peer.Id, out var player))
        {
            _ = _gameService.DisconnectPlayer(player.PlayerId);
        }
    }
    
    public async void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        try
        {
            var messageType = (MessageType)reader.GetByte();
            
            switch (messageType)
            {
                case MessageType.Connect:
                    await HandleConnect(peer, reader);
                    break;
                    
                case MessageType.PlayerInput:
                    await HandlePlayerInput(peer, reader);
                    break;
                    
                case MessageType.Heartbeat:
                    HandleHeartbeat(peer);
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
    
    private async Task HandleConnect(NetPeer peer, NetPacketReader reader)
    {
        var playerId = reader.GetString();
        
        var connected = await _gameService.ConnectPlayer(playerId);
        if (connected)
        {
            var player = new ConnectedPlayer
            {
                PlayerId = playerId,
                Peer = peer,
                LastHeartbeat = DateTime.UtcNow
            };
            
            _connectedPlayers[peer.Id] = player;
            
            // Send connection success
            _writer.Reset();
            _writer.Put((byte)MessageType.ConnectSuccess);
            _writer.Put(playerId);
            peer.Send(_writer, DeliveryMethod.ReliableOrdered);
            
            _logger.LogInformation("Player {PlayerId} connected via UDP", playerId);
        }
        else
        {
            // Send connection failure
            _writer.Reset();
            _writer.Put((byte)MessageType.ConnectFailed);
            _writer.Put("Failed to connect player");
            peer.Send(_writer, DeliveryMethod.ReliableOrdered);
            
            peer.Disconnect();
        }
    }
    
    private async Task HandlePlayerInput(NetPeer peer, NetPacketReader reader)
    {
        if (!_connectedPlayers.TryGetValue(peer.Id, out var player))
        {
            _logger.LogWarning("Received input from unknown peer");
            return;
        }
        
        var moveX = reader.GetFloat();
        var moveY = reader.GetFloat();
        var isShooting = reader.GetBool();
        
        var moveDirection = new Vector2(moveX, moveY);
        await _gameService.UpdatePlayerInput(player.PlayerId, moveDirection, isShooting);
    }
    
    private void HandleHeartbeat(NetPeer peer)
    {
        if (_connectedPlayers.TryGetValue(peer.Id, out var player))
        {
            player.LastHeartbeat = DateTime.UtcNow;
        }
    }
    
    public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
    {
        _logger.LogError("Network error from {EndPoint}: {Error}", endPoint, socketError);
    }
    
    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
    {
        // Handle discovery requests
        if (messageType == UnconnectedMessageType.BasicMessage)
        {
            var request = reader.GetString();
            if (request == "SHOOTER_DISCOVERY")
            {
                _writer.Reset();
                _writer.Put("SHOOTER_SERVER");
                _writer.Put(_udpPort);
                _netManager?.SendUnconnectedMessage(_writer, remoteEndPoint);
            }
        }
    }
    
    public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
    {
        // Could track latency for each player if needed
    }
    
    public void OnConnectionRequest(ConnectionRequest request)
    {
        _logger.LogInformation("Connection request from {EndPoint} with key: {Key}", request.RemoteEndPoint, request.Data.GetString());
        
        // Check for valid connection key
        if (request.Data.GetString() == "SHOOTER_CONNECT")
        {
            request.Accept();
            _logger.LogInformation("Accepted connection from {EndPoint}", request.RemoteEndPoint);
        }
        else
        {
            request.Reject();
            _logger.LogWarning("Rejected connection from {EndPoint} - invalid key", request.RemoteEndPoint);
        }
    }
}

