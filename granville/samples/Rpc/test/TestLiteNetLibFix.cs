#!/usr/bin/env dotnet-script
#r "nuget: LiteNetLib, 1.3.1"

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using LiteNetLib;

// Simple test to verify LiteNetLib basic functionality
Console.WriteLine("=== Testing LiteNetLib Connection Fix ===");

class TestServer : INetEventListener
{
    private NetManager server;
    private bool connected = false;
    
    public void Start()
    {
        server = new NetManager(this);
        server.Start(12000);
        Console.WriteLine("✅ Test server started on port 12000");
    }
    
    public void Stop()
    {
        server?.Stop();
        Console.WriteLine("Server stopped");
    }
    
    public void Poll()
    {
        server?.PollEvents();
    }
    
    public bool IsConnected => connected;
    
    public void OnConnectionRequest(ConnectionRequest request)
    {
        string key = "";
        try
        {
            if (request.Data.AvailableBytes > 0)
            {
                key = request.Data.GetString();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Server: Error reading connection key: {ex.Message}");
        }
        
        Console.WriteLine($"📥 Server: Connection request from {request.RemoteEndPoint} with key '{key}' (bytes: {request.Data.AvailableBytes})");
        
        if (string.IsNullOrEmpty(key) || key == "RpcConnection")
        {
            Console.WriteLine($"✅ Server: Accepting connection with key '{key}'");
            request.Accept();
        }
        else
        {
            Console.WriteLine($"❌ Server: Rejecting connection with invalid key '{key}'");
            request.Reject();
        }
    }
    
    public void OnPeerConnected(NetPeer peer)
    {
        Console.WriteLine($"🎉 Server: Peer connected - {peer.Address}:{peer.Port}");
        connected = true;
    }
    
    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        Console.WriteLine($"👋 Server: Peer disconnected - {peer.Address}:{peer.Port}, reason: {disconnectInfo.Reason}");
        connected = false;
    }
    
    public void OnNetworkError(IPEndPoint endPoint, System.Net.Sockets.SocketError socketError) 
    {
        Console.WriteLine($"❌ Server: Network error from {endPoint}: {socketError}");
    }
    
    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod) 
    {
        var message = reader.GetString();
        Console.WriteLine($"📨 Server: Received message: '{message}'");
        reader.Recycle();
    }
    
    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) 
    {
        reader.Recycle();
    }
    
    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
}

class TestClient : INetEventListener
{
    private NetManager client;
    private NetPeer serverPeer;
    private bool connected = false;
    private TaskCompletionSource<bool> connectTcs;
    
    public async Task<bool> ConnectAsync(string connectionKey = "RpcConnection")
    {
        connectTcs = new TaskCompletionSource<bool>();
        
        client = new NetManager(this);
        client.Start();
        Console.WriteLine("✅ Test client started");
        
        Console.WriteLine($"🔗 Client: Connecting to 127.0.0.1:12000 with key '{connectionKey}'");
        serverPeer = client.Connect("127.0.0.1", 12000, connectionKey);
        
        if (serverPeer == null)
        {
            Console.WriteLine("❌ Client: Connect returned null");
            return false;
        }
        
        // Start polling
        _ = Task.Run(async () =>
        {
            while (client != null)
            {
                client.PollEvents();
                await Task.Delay(15);
            }
        });
        
        // Wait for connection with timeout
        using var cts = new CancellationTokenSource(5000);
        try
        {
            return await connectTcs.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("❌ Client: Connection timed out");
            return false;
        }
    }
    
    public void Stop()
    {
        serverPeer?.Disconnect();
        client?.Stop();
        Console.WriteLine("Client stopped");
    }
    
    public bool IsConnected => connected;
    
    public void OnConnectionRequest(ConnectionRequest request) 
    {
        request.Reject();
    }
    
    public void OnPeerConnected(NetPeer peer)
    {
        Console.WriteLine($"🎉 Client: Connected to server - {peer.Address}:{peer.Port}");
        connected = true;
        connectTcs?.SetResult(true);
    }
    
    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        Console.WriteLine($"👋 Client: Disconnected from server - {peer.Address}:{peer.Port}, reason: {disconnectInfo.Reason}");
        connected = false;
        connectTcs?.SetResult(false);
    }
    
    public void OnNetworkError(IPEndPoint endPoint, System.Net.Sockets.SocketError socketError) 
    {
        Console.WriteLine($"❌ Client: Network error from {endPoint}: {socketError}");
        connectTcs?.SetException(new Exception($"Network error: {socketError}"));
    }
    
    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod) 
    {
        var message = reader.GetString();
        Console.WriteLine($"📨 Client: Received message: '{message}'");
        reader.Recycle();
    }
    
    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) 
    {
        reader.Recycle();
    }
    
    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
}

// Test the connection
var server = new TestServer();
server.Start();

// Start server polling
var serverTask = Task.Run(async () =>
{
    while (true)
    {
        server.Poll();
        await Task.Delay(15);
        if (server.IsConnected) break;
    }
});

await Task.Delay(100); // Let server start

// Test 1: Connection with proper key
Console.WriteLine("\n🧪 Test 1: Connection with 'RpcConnection' key");
var client1 = new TestClient();
var result1 = await client1.ConnectAsync("RpcConnection");
Console.WriteLine($"Result: {(result1 ? "✅ SUCCESS" : "❌ FAILED")}");

await Task.Delay(500);
client1.Stop();

// Test 2: Connection with empty key (should work for backward compatibility)
Console.WriteLine("\n🧪 Test 2: Connection with empty key");
var client2 = new TestClient();
var result2 = await client2.ConnectAsync("");
Console.WriteLine($"Result: {(result2 ? "✅ SUCCESS" : "❌ FAILED")}");

await Task.Delay(500);
client2.Stop();

// Test 3: Connection with wrong key (should fail)
Console.WriteLine("\n🧪 Test 3: Connection with invalid key");
var client3 = new TestClient();
var result3 = await client3.ConnectAsync("WRONG_KEY");
Console.WriteLine($"Result: {(result3 ? "❌ UNEXPECTED SUCCESS" : "✅ CORRECTLY FAILED")}");

client3.Stop();
server.Stop();

Console.WriteLine("\n=== Test Complete ===");
Console.WriteLine($"Test 1 (RpcConnection): {(result1 ? "PASS" : "FAIL")}");
Console.WriteLine($"Test 2 (Empty key): {(result2 ? "PASS" : "FAIL")}");
Console.WriteLine($"Test 3 (Invalid key): {(!result3 ? "PASS" : "FAIL")}");

if (result1 && result2 && !result3)
{
    Console.WriteLine("🎉 ALL TESTS PASSED! LiteNetLib connection key fix is working!");
}
else
{
    Console.WriteLine("❌ Some tests failed. Connection fix needs more work.");
}