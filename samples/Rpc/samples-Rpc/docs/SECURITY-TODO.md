# Security TODOs for Shooter Demo

This document outlines security-related tasks that need to be implemented in the Shooter demo to demonstrate and validate Forkleans.Rpc security features.

## High Priority - Authentication & Authorization

### 1. Implement Authentication System
- [ ] Add JWT token generation for clients and ActionServers
- [ ] Create authentication service in Silo
- [ ] Add login endpoint for clients
- [ ] Implement ActionServer authentication with "action-server" role
- [ ] Add token validation middleware

### 2. Apply Authorization Attributes
- [ ] Add `[AuthenticationRequired(false)]` to public game query methods (e.g., GetZoneInfo)
- [ ] Add `[Authorize("action-server")]` to inter-ActionServer communication methods
- [ ] Ensure all player action methods require authentication by default
- [ ] Create example of mixed-access interface with both public and protected methods

### 3. Implement ClientCreatable Control
- [ ] Add `[ClientCreatable]` attribute to IGameRpcGrain
- [ ] Remove `[ClientCreatable]` from any infrastructure grains
- [ ] Add enforcement logic to prevent unauthorized grain creation
- [ ] Add unit tests to verify ClientCreatable enforcement

## Medium Priority - Input Validation & Rate Limiting

### 4. Input Validation
- [ ] Add validation for all player movement commands
- [ ] Validate weapon firing rates and projectile spawning
- [ ] Implement bounds checking for zone transitions
- [ ] Add sanity checks for player stats (HP, position, etc.)

### 5. Rate Limiting
- [ ] Implement per-player rate limiting for actions
- [ ] Add rate limiting for projectile creation
- [ ] Limit zone transition frequency
- [ ] Add rate limiting for chat/communication features (if any)

### 6. Serialization Safety
- [ ] Configure type whitelist for allowed serialization types
- [ ] Add size limits for incoming messages
- [ ] Implement nesting depth limits
- [ ] Add logging for rejected serialization attempts

## Low Priority - Monitoring & Hardening

### 7. Security Logging
- [ ] Log all authentication attempts (success/failure)
- [ ] Log authorization failures with details
- [ ] Track suspicious patterns (rapid zone changes, impossible movements)
- [ ] Add metrics for rate limit violations

### 8. Anti-Cheat Measures
- [ ] Implement server-side physics validation
- [ ] Add detection for teleportation/speed hacks
- [ ] Validate damage calculations server-side
- [ ] Check for impossible game states

### 9. DoS Protection
- [ ] Implement connection limits per IP
- [ ] Add timeout for idle connections
- [ ] Limit concurrent grains per player
- [ ] Add resource quotas for ActionServers

## Testing & Documentation

### 10. Security Tests
- [ ] Write unit tests for authentication flow
- [ ] Add integration tests for authorization scenarios
- [ ] Create tests for rate limiting behavior
- [ ] Add tests for malformed input handling

### 11. Security Documentation
- [ ] Document authentication flow for game developers
- [ ] Create security best practices guide for Shooter
- [ ] Add inline comments explaining security decisions
- [ ] Create troubleshooting guide for common security issues

## Example Implementation Priority

1. **Phase 1** (Do First):
   - Basic JWT authentication (#1)
   - Apply `[AuthenticationRequired]` to existing interfaces (#2)
   - Basic input validation for player actions (#4)

2. **Phase 2** (After Phase 1):
   - Implement `[ClientCreatable]` system (#3)
   - Add rate limiting infrastructure (#5)
   - Enhanced validation and anti-cheat (#8)

3. **Phase 3** (Polish):
   - Security logging and monitoring (#7)
   - DoS protection measures (#9)
   - Comprehensive testing (#10)

## Notes for Implementation

- Start with authentication to establish identity before implementing authorization
- Use the Shooter demo as a testbed for security features before promoting to core Forkleans.Rpc
- Consider performance impact of security features, especially for real-time gameplay
- Ensure security features are configurable for different deployment scenarios