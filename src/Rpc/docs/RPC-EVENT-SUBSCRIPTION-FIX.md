# RPC Event Subscription Race Condition Fix

## Problem Description

The RPC client was experiencing a race condition where UDP packets would arrive from the server immediately after connection establishment, but before the event handlers were properly subscribed. This caused the client to miss critical messages like handshake acknowledgments, resulting in hanging connections.

### Symptoms
- Bot hangs during RPC client startup after "Starting grain acquisition"
- LiteNetLib logs show "received X bytes from server" but DataReceived event has 0 subscribers
- Client never receives handshake acknowledgment despite server sending it

### Root Cause

In `RpcClient.ConnectToServerAsync()`, the order of operations was:
1. Create transport via factory
2. Create RpcConnection wrapper (which subscribes to transport events)
3. Call `transport.ConnectAsync()` - **Data can arrive here!**
4. Add transport to tracking dictionary
5. Add connection to connection manager

The problem: Steps 4-5 happened AFTER the connection was established, creating a window where data could arrive but wouldn't be properly handled.

## Solution

Reorder operations to ensure all event subscriptions and tracking are set up BEFORE establishing the connection:

```csharp
// Create transport and connection wrapper
var transport = _transportFactory.CreateTransport(_serviceProvider);
var connection = new RpcConnection(serverId, endpoint, transport, _logger);

// Subscribe to events
connection.DataReceived += OnDataReceived;
connection.ConnectionEstablished += OnConnectionEstablished;
connection.ConnectionClosed += OnConnectionClosed;

// Track transport and connection BEFORE connecting
_transports[serverId] = transport;
await _connectionManager.AddConnectionAsync(serverId, connection);

// NOW it's safe to connect - all handlers are ready
await transport.ConnectAsync(endpoint, cancellationToken);
```

## Impact

This fix ensures:
1. Event handlers are subscribed before any data can arrive
2. The connection manager knows about the connection before it's active
3. No messages are lost due to timing issues
4. The handshake/manifest exchange completes reliably

## Testing

The fix was identified through:
1. Enhanced logging showing LiteNetLib receiving data but with 0 event subscribers
2. Analysis of the event subscription chain from LiteNetLib → Transport → RpcConnection → RpcClient
3. Simple UDP tests proving basic connectivity worked
4. Tracing the exact sequence of operations during connection establishment

## Related Files

- `/src/Rpc/Orleans.Rpc.Client/RpcClient.cs` - Contains the fix
- `/src/Rpc/Orleans.Rpc.Transport.LiteNetLib/LiteNetLibClientTransport.cs` - Where data is received
- `/src/Rpc/Orleans.Rpc.Client/RpcConnection.cs` - Event forwarding layer