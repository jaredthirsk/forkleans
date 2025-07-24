#!/usr/bin/env dotnet-script
#r "nuget: Microsoft.Extensions.Logging.Console, 9.0.0"
#r "nuget: Microsoft.Extensions.DependencyInjection, 9.0.0"
#r "nuget: LiteNetLib, 1.3.1"

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using LiteNetLib;

// Direct RPC transport connection test using LiteNetLib to validate fix
Console.WriteLine("=== Direct RPC Transport Connection Test ===");

// Minimal LiteNetLib client transport simulation
class SimpleLiteNetLibClient : INetEventListener
{
    private readonly ILogger _logger;
    private NetManager _netManager;
    private TaskCompletionSource<bool> _connectionTcs;
    private bool _connected = false;

    public SimpleLiteNetLibClient(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<bool> ConnectAsync(IPEndPoint endpoint, string connectionKey, int timeoutMs = 5000)
    {
        _logger.LogInformation("Starting LiteNetLib client connection to {Endpoint} with key '{Key}'", endpoint, connectionKey);
        
        _connectionTcs = new TaskCompletionSource<bool>();
        
        _netManager = new NetManager(this)
        {
            AutoRecycle = true,
            EnableStatistics = true,
            UnconnectedMessagesEnabled = false,
        };

        if (!_netManager.Start())
        {
            _logger.LogError("Failed to start NetManager");
            return false;
        }
        
        _logger.LogDebug("NetManager started, initiating connection");
        
        // Start polling
        _ = Task.Run(async () =>
        {
            while (_netManager != null && !_connected)
            {
                _netManager.PollEvents();
                await Task.Delay(15);
            }
        });

        // Connect with the specified key
        var peer = _netManager.Connect(endpoint.Address.ToString(), endpoint.Port, connectionKey);
        if (peer == null)
        {
            _logger.LogError("NetManager.Connect returned null");
            return false;
        }

        _logger.LogDebug("Connection initiated, waiting for result");

        // Wait for connection with timeout
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            var result = await _connectionTcs.Task.WaitAsync(cts.Token);
            _logger.LogInformation("Connection result: {Result}", result);
            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("Connection timed out after {TimeoutMs}ms", timeoutMs);
            return false;
        }
    }

    public void Dispose()
    {
        _netManager?.Stop();
        _netManager = null;
    }

    #region INetEventListener
    
    public void OnPeerConnected(NetPeer peer)
    {
        _logger.LogInformation("✅ Connected to server: {Address}:{Port}", peer.Address, peer.Port);
        _connected = true;
        _connectionTcs?.TrySetResult(true);
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        _logger.LogInformation("❌ Disconnected from server: {Address}:{Port}, reason: {Reason}", 
            peer.Address, peer.Port, disconnectInfo.Reason);
        _connected = false;
        _connectionTcs?.TrySetResult(false);
    }

    public void OnNetworkError(IPEndPoint endPoint, System.Net.Sockets.SocketError socketError)
    {
        _logger.LogError("Network error from {Endpoint}: {Error}", endPoint, socketError);
        _connectionTcs?.TrySetException(new Exception($"Network error: {socketError}"));
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod) 
    {
        reader.Recycle();
    }

    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) 
    {
        reader.Recycle();
    }

    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
    
    public void OnConnectionRequest(ConnectionRequest request) 
    {
        request.Reject();
    }
    
    #endregion
}

// Setup logging
var services = new ServiceCollection();
services.AddLogging(builder => 
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);
});

var serviceProvider = services.BuildServiceProvider();
var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
var logger = loggerFactory.CreateLogger("RpcConnectionTest");

// Test connection to the running ActionServer RPC port
var endpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 12000);

logger.LogInformation("Testing connection to ActionServer at {Endpoint}", endpoint);

// Test 1: Empty connection key (as used by the original broken LiteNetLibTransport)
logger.LogInformation("\n🧪 Test 1: Empty connection key (original broken behavior)");
var client1 = new SimpleLiteNetLibClient(logger);
var result1 = await client1.ConnectAsync(endpoint, "", 10000);
client1.Dispose();

logger.LogInformation("Test 1 result: {Result}", result1 ? "✅ SUCCESS" : "❌ FAILED");

// Test 2: RpcConnection key (as used by LiteNetLibClientTransport)
logger.LogInformation("\n🧪 Test 2: 'RpcConnection' key (fixed behavior)");
var client2 = new SimpleLiteNetLibClient(logger);  
var result2 = await client2.ConnectAsync(endpoint, "RpcConnection", 10000);
client2.Dispose();

logger.LogInformation("Test 2 result: {Result}", result2 ? "✅ SUCCESS" : "❌ FAILED");

// Summary
logger.LogInformation("\n=== Test Results ===");
logger.LogInformation("Empty key test: {Result}", result1 ? "PASS" : "FAIL");
logger.LogInformation("RpcConnection key test: {Result}", result2 ? "PASS" : "FAIL");

if (result2)
{
    logger.LogInformation("🎉 SUCCESS: RPC connection works with 'RpcConnection' key!");
    logger.LogInformation("The bot should now be able to connect with the fixed LiteNetLib transport.");
}
else if (result1)
{
    logger.LogInformation("⚠️  PARTIAL: Empty key works, but this suggests server accepts all connections");
}
else
{
    logger.LogError("❌ FAILED: Neither connection method works. ActionServer may not be running on port 12000.");
    logger.LogError("Make sure ActionServer is running before testing bot connection.");
}