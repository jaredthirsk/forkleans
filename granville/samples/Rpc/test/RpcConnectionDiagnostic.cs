using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

// Simple RPC connection diagnostic test
Console.WriteLine("=== RPC Connection Diagnostic Test ===");

async Task TestUdpConnection()
{
    Console.WriteLine("Testing UDP connection to 127.0.0.1:12000");
    
    try
    {
        using var udpClient = new UdpClient();
        Console.WriteLine("Created UDP client");
        
        var serverEndpoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 12000);
        Console.WriteLine($"Target endpoint: {serverEndpoint}");
        
        // Test basic UDP connectivity by trying to send data
        Console.WriteLine("Attempting to send test packet via UDP");
        var testData = System.Text.Encoding.UTF8.GetBytes("TEST");
        await udpClient.SendAsync(testData, testData.Length, serverEndpoint);
        Console.WriteLine("✅ Test packet sent successfully");
        
        // Try to receive response (with timeout)
        Console.WriteLine("Waiting for response (2 second timeout)");
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        
        try
        {
            var result = await udpClient.ReceiveAsync().WaitAsync(cts.Token);
            Console.WriteLine($"✅ Received response: {result.Buffer.Length} bytes from {result.RemoteEndPoint}");
        }
        catch (TimeoutException)
        {
            Console.WriteLine("⚠️  No response received within 2 seconds (normal for raw UDP test)");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("⚠️  No response received within 2 seconds (normal for raw UDP test)");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ UDP connection test failed: {ex.Message}");
        Console.WriteLine($"Exception type: {ex.GetType().Name}");
    }
}

async Task TestTcpConnection()
{
    Console.WriteLine("Testing TCP connection to 127.0.0.1:12000");
    
    try
    {
        using var tcpClient = new TcpClient();
        Console.WriteLine("Created TCP client");
        
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        
        Console.WriteLine("Attempting TCP connection");
        await tcpClient.ConnectAsync("127.0.0.1", 12000, cts.Token);
        Console.WriteLine("✅ TCP connection established (unexpected - should be UDP only)");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"✅ TCP connection failed as expected: {ex.Message}");
    }
}

void TestPortListening()
{
    Console.WriteLine("Checking if port 12000 is listening");
    
    try
    {
        using var udpClient = new UdpClient();
        
        // Try to bind to port 12000 to see if it's already in use
        try
        {
            udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, 12000));
            Console.WriteLine("⚠️  Port 12000 is NOT in use (could bind to it)");
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            Console.WriteLine("✅ Port 12000 is in use (as expected)");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Error checking port status: {ex.Message}");
    }
}

// Check what's listening on various ports
void CheckListeningPorts()
{
    Console.WriteLine("Checking listening ports with netstat...");
    try
    {
        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "netstat",
                Arguments = "-ln | grep :12000",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }
        };
        
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        
        if (!string.IsNullOrEmpty(output))
        {
            Console.WriteLine($"✅ netstat shows port 12000 is listening:");
            Console.WriteLine(output);
        }
        else
        {
            Console.WriteLine("⚠️  netstat shows no process listening on port 12000");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Error running netstat: {ex.Message}");
    }
}

// Run diagnostic tests
Console.WriteLine("Starting RPC connection diagnostics...");

TestPortListening();
CheckListeningPorts();
await TestTcpConnection();
await TestUdpConnection();

Console.WriteLine("=== Diagnostic Complete ===");