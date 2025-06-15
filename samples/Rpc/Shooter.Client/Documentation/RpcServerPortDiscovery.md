# RPC Server Port Discovery in Forkleans

## Overview

When using Forkleans RPC server with dynamic port allocation (port 0), you need a way to discover the actual port that the server is listening on after it starts. This document explains the available approaches and current limitations.

## Current Implementation

The Shooter.ActionServer sample uses the following approach:

1. **RpcServerPortProvider**: A singleton service that stores the RPC server port
2. **Configuration**: The port is set during server configuration (not dynamic discovery)
3. **Registration**: The ActionServerRegistrationService uses this port to register with the Orleans silo

## Limitation with Dynamic Ports

Currently, when you configure the RPC server with port 0 (dynamic allocation):

```csharp
rpcBuilder.ConfigureEndpoint(0); // Let OS assign a port
```

The actual port assigned by the OS is not automatically discovered. The current implementation has these limitations:

1. The `ILocalRpcServerDetails` contains the configured port (0), not the actual bound port
2. The LiteNetLib transport doesn't expose the actual port after binding
3. There's no built-in lifecycle event that provides the actual port

## Workarounds

### 1. Use a Fixed Port

The simplest solution is to use a fixed port:

```csharp
rpcBuilder.ConfigureEndpoint(5555); // Use a specific port
```

### 2. Implement Port Discovery in Transport

To properly support dynamic ports, the transport layer would need to be enhanced:

```csharp
// In LiteNetLibTransport.cs after _netManager.Start()
if (_serverPort == 0)
{
    // Get the actual port from the socket
    var actualPort = _netManager.LocalPort; // This property would need to be exposed by LiteNetLib
    // Notify listeners of the actual port
}
```

### 3. Use the Provided Services

The example code provides these services:

- **RpcServerPortService**: Participates in the RPC server lifecycle
- **RpcServerPortProvider**: Stores and provides access to the port
- **RpcServerPortUpdateService**: Updates the provider when the server starts

## Example Usage

```csharp
// In Program.cs
builder.Services.AddRpcServerPortTracking();
builder.Services.AddRpcServerPortProvider();

// In your service
public class MyService
{
    private readonly RpcServerPortProvider _portProvider;
    
    public async Task DoSomethingWithPort()
    {
        var port = await _portProvider.WaitForPortAsync(TimeSpan.FromSeconds(10));
        Console.WriteLine($"RPC server is listening on port {port}");
    }
}
```

## Future Improvements

To fully support dynamic port discovery, these enhancements would be needed:

1. **Transport Enhancement**: Modify IRpcTransport to expose the actual bound endpoint
2. **Lifecycle Event**: Add a lifecycle stage after binding that provides the actual endpoint
3. **Server Details Update**: Update ILocalRpcServerDetails after the transport binds

## Current Best Practice

For production use, it's recommended to:

1. Use fixed ports configured through your deployment system
2. Use environment variables or configuration files to set the port
3. Register the configured port with your service discovery system

```csharp
var rpcPort = builder.Configuration.GetValue<int>("RpcServer:Port", 5555);
rpcBuilder.ConfigureEndpoint(rpcPort);
```

This ensures predictable behavior and proper service discovery integration.