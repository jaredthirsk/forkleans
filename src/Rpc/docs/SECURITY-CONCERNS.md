# Forkleans.Rpc Security Concerns and Mitigations

## Overview

This document outlines security concerns specific to Forkleans.Rpc, particularly in the context of game development where untrusted clients connect to servers. As noted by Orleans lead developer Reuben Bond, Orleans was not built with security as a primary concern, which makes these considerations crucial for Forkleans.Rpc.

## Primary Security Concerns

### 1. Serialization Safety

**Concern**: Rogue clients could potentially serialize arbitrary and dangerous objects as JSON over the wire to ActionServer or other RPC servers.

**Risks**:
- Deserialization of unexpected types could lead to code execution
- Large or deeply nested objects could cause denial of service
- Malformed data could crash the server
- Type confusion attacks

**Potential Mitigations**:
- **Type Whitelisting**: Implement a strict whitelist of allowed types for deserialization
- **Schema Validation**: Use JSON Schema or similar validation before deserialization
- **Size Limits**: Enforce maximum message size and nesting depth
- **Safe Deserializer Configuration**: Configure serializers to reject unknown types
- **Input Sanitization**: Validate all inputs post-deserialization

### 2. Unauthorized Grain Instantiation

**Concern**: Unlike traditional Orleans usage, RPC clients should not be able to instantiate grains on demand, as this could lead to resource exhaustion attacks.

**Proposed Solution**: Implement a `[ClientCreatable]` attribute system where:
- By default, clients cannot create new grain instances
- Only grains explicitly marked with `[ClientCreatable]` can be instantiated by clients
- Server-side code can always create any grain

**Implementation Approach**:
```csharp
[AttributeUsage(AttributeTargets.Interface)]
public class ClientCreatableAttribute : Attribute { }

// Example usage:
[ClientCreatable]
public interface IPlayerSessionGrain : IGrainWithGuidKey { }
```

### 3. Access Control to Infrastructure Grains

**Concern**: In traditional Orleans, clients might have access to core silo infrastructure grains.

**Current Status**: This appears to be less of an issue in Forkleans.Rpc due to architectural differences, but requires verification.

**Verification Steps**:
- Audit all grains accessible via RPC
- Ensure infrastructure grains are not exposed through RPC interfaces
- Implement access control lists for grain methods

### 4. Denial of Service (DoS) Attacks

**Concerns**:
- Rate limiting of RPC calls
- Resource consumption per client
- Connection flooding
- Computational complexity attacks

**Potential Mitigations**:
- **Rate Limiting**: Implement per-client rate limits
- **Connection Limits**: Maximum connections per IP
- **Request Throttling**: Limit concurrent requests per client
- **Timeout Management**: Strict timeouts for all operations
- **Resource Quotas**: Memory and CPU limits per client session

### 5. Authentication and Authorization

**Concerns**:
- Client identity verification
- Method-level access control
- Session management
- Token replay attacks
- Differentiating between trusted servers and untrusted clients

**Planned Authorization System**:

Forkleans.Rpc will implement an attribute-based authorization system with two key attributes:

1. **`[AuthenticationRequired(bool required = true)]`**: Controls whether authentication is required
   - When `false`, allows unauthenticated clients to call the method
   - When `true` (default), requires authentication
   - Can be applied at interface or method level

2. **`[Authorize(params string[] roles)]`**: Requires specific roles for access
   - Requires the caller to be authenticated
   - Caller must have at least one of the specified roles
   - Can be applied at interface or method level
   - Method-level attributes override interface-level attributes

**Example Usage**:

```csharp
// Public interface accessible to all clients
[AuthenticationRequired(false)]
public interface IGameLobbyGrain : IGrainWithGuidKey
{
    Task<GameList> GetAvailableGames();
    Task<bool> JoinGame(string gameId);
    
    // This method requires authentication despite interface setting
    [AuthenticationRequired(true)]
    [Authorize("player", "admin")]
    Task<PlayerProfile> GetPlayerProfile();
}

// Interface requiring authentication by default
public interface IGameSessionGrain : IGrainWithGuidKey
{
    Task<GameState> GetGameState();
    Task<bool> MakeMove(PlayerMove move);
    
    // Admin-only method
    [Authorize("admin")]
    Task ResetGame();
}

// Server-to-server interface (e.g., ActionServer to ActionServer)
[Authorize("action-server")]
public interface IActionServerCoordinatorGrain : IGrainWithGuidKey
{
    Task<ZoneTransferData> TransferPlayer(Guid playerId, string fromZone, string toZone);
    Task<bool> ValidateServerHealth(string serverId);
    Task SyncZoneBoundaries(ZoneBoundaryData data);
}

// Mixed access interface
public interface IPlayerManagementGrain : IGrainWithGuidKey
{
    // Players can view their own stats
    [Authorize("player", "admin")]
    Task<PlayerStats> GetPlayerStats();
    
    // Only admins can modify stats
    [Authorize("admin")]
    Task UpdatePlayerStats(PlayerStats stats);
    
    // Server-to-server communication for stat tracking
    [Authorize("action-server", "stats-server")]
    Task RecordPlayerAction(PlayerActionEvent action);
}
```

**Implementation in Shooter Example**:

In the Shooter sample, this system enables:
- **Clients**: Can only call methods marked with `[AuthenticationRequired(false)]` or authorized with "player" role
- **ActionServers**: Authenticate with "action-server" role to call privileged inter-server methods
- **Admin Tools**: Use "admin" role for game management operations

Example ActionServer-to-ActionServer communication:
```csharp
// Only ActionServers can call this
[Authorize("action-server")]
public interface IZoneCoordinatorGrain : IGrainWithGuidKey
{
    Task<bool> RegisterActionServer(string serverId, ZoneInfo zoneInfo);
    Task<ZoneTransferResult> InitiateZoneTransfer(Guid entityId, string targetZone);
    Task SyncEntityState(Guid entityId, EntityState state);
}
```

**Additional Mitigations**:
- **JWT Integration**: Use JSON Web Tokens for client authentication
- **Session Tokens**: Short-lived, rotatable session tokens
- **IP Whitelisting**: Optional IP-based access control for server-to-server communication
- **Certificate-based Authentication**: For ActionServer-to-ActionServer trust

## Application-Level Mitigations

### For Game Developers Using Forkleans.Rpc

1. **Input Validation**: Always validate client inputs in grain methods
2. **State Boundaries**: Never trust client-provided state
3. **Action Validation**: Verify all game actions server-side
4. **Rate Limiting**: Implement game-specific rate limits
5. **Monitoring**: Log suspicious activities for analysis

### Example Secure Grain Implementation

```csharp
[ClientCreatable]
public interface IPlayerActionGrain : IGrainWithGuidKey
{
    Task<bool> PerformAction(PlayerAction action);
}

public class PlayerActionGrain : Grain, IPlayerActionGrain
{
    private readonly IRateLimiter _rateLimiter;
    
    public async Task<bool> PerformAction(PlayerAction action)
    {
        // Rate limiting
        if (!await _rateLimiter.AllowRequest(this.GetPrimaryKey()))
            return false;
            
        // Input validation
        if (!IsValidAction(action))
            return false;
            
        // Business logic validation
        if (!await CanPerformAction(action))
            return false;
            
        // Execute action
        return await ExecuteAction(action);
    }
}
```

## Next Steps for Forkleans.Rpc Development

### Phase 1: Foundation (Immediate)
1. Implement `[ClientCreatable]` attribute and enforcement
2. Add configuration for type whitelisting in serialization
3. Implement basic rate limiting infrastructure
4. Add request size limits

### Phase 2: Enhanced Security (Short-term)
1. Develop authentication middleware for RPC
2. Implement `[AuthenticationRequired]` and `[Authorize]` attributes with role-based access control
3. Add connection and request throttling
4. Create security-focused logging infrastructure
5. Implement server-to-server authentication for ActionServer communication

### Phase 3: Advanced Features (Medium-term)
1. Integrate with popular auth providers (JWT, OAuth2)
2. Implement IP-based access control
3. Add anomaly detection for suspicious patterns
4. Create security dashboard for monitoring

### Phase 4: Hardening (Long-term)
1. Security audit by external firm
2. Penetration testing framework
3. Automated security regression tests
4. Security best practices documentation

## Configuration Example

```json
{
  "Forkleans.Rpc": {
    "Security": {
      "EnableClientCreatable": true,
      "MaxRequestSize": 1048576,
      "MaxNestingDepth": 32,
      "RateLimiting": {
        "RequestsPerMinute": 600,
        "BurstSize": 20
      },
      "Serialization": {
        "AllowedTypes": [
          "Shooter.Shared.PlayerAction",
          "Shooter.Shared.GameState"
        ]
      }
    }
  }
}
```

## Testing Security

### Unit Tests
- Test serialization with malformed data
- Verify rate limiting works correctly
- Test authorization attributes
- Verify ClientCreatable enforcement

### Integration Tests
- DoS simulation tests
- Authentication flow tests
- Authorization boundary tests
- Serialization attack tests

### Performance Tests
- Impact of security features on latency
- Throughput with security enabled
- Resource usage under attack scenarios

## References

- [OWASP Deserialization Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Deserialization_Cheat_Sheet.html)
- [Microsoft Security Best Practices](https://docs.microsoft.com/en-us/aspnet/core/security/)
- [Game Security Best Practices](https://www.gdcvault.com/play/1024994/Practical-Security-for-Online-Games)