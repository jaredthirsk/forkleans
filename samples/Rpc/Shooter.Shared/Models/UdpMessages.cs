namespace Shooter.Shared.Models;

/// <summary>
/// Message types for UDP communication between client and server
/// </summary>
public enum MessageType : byte
{
    Connect = 1,
    ConnectSuccess = 2,
    ConnectFailed = 3,
    ConnectionAccepted = 4,
    Disconnect = 5,
    PlayerInput = 10,
    WorldStateUpdate = 20,
    Heartbeat = 30,
    ServerInfo = 40
}