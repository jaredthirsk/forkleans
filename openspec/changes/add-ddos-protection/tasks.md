## 1. Rate Limiter Infrastructure (Orleans.Rpc.Security)

- [ ] 1.1 Create `RateLimiting/IRateLimiter.cs` interface with `TryAcquire()` and `GetRemainingQuota()`
- [ ] 1.2 Create `RateLimiting/SlidingWindowRateLimiter.cs` implementation
- [ ] 1.3 Create `RateLimiting/RateLimitOptions.cs` with limit, window size, burst allowance
- [ ] 1.4 Create `RateLimiting/RateLimitResult.cs` (Allowed, Denied, RetryAfter)
- [ ] 1.5 Unit tests for sliding window algorithm edge cases

## 2. IP Blocklist Infrastructure (Orleans.Rpc.Security)

- [ ] 2.1 Create `Blocklist/IIpBlocklist.cs` interface
- [ ] 2.2 Create `Blocklist/InMemoryIpBlocklist.cs` with permanent and temporary entries
- [ ] 2.3 Create `Blocklist/BlocklistEntry.cs` with IP, reason, expiry
- [ ] 2.4 Implement automatic cleanup of expired temporary blocks
- [ ] 2.5 Add configuration loading for permanent blocklist from appsettings

## 3. Transport Layer Protection (Orleans.Rpc.Transport)

- [ ] 3.1 Create `Protection/ConnectionRateLimiter.cs` (per-IP connection rate)
- [ ] 3.2 Modify `LiteNetLibTransport.OnConnectionRequest()` to check rate limiter
- [ ] 3.3 Modify `LiteNetLibTransport.OnConnectionRequest()` to check blocklist
- [ ] 3.4 Enforce `RpcServerOptions.MaxConnections` (currently unused)
- [ ] 3.5 Add connection timeout enforcement for stale connections
- [ ] 3.6 Add metrics: connections_rejected_total, connections_rate_limited_total

## 4. Packet Layer Protection (Orleans.Rpc.Server)

- [ ] 4.1 Add message size validation in `RpcServer.OnDataReceived()` (reject > 64KB)
- [ ] 4.2 Create per-connection message rate limiter
- [ ] 4.3 Add deserialization timeout (CancellationToken with 100ms default)
- [ ] 4.4 Add metrics: messages_rejected_size_total, messages_rate_limited_total

## 5. Request Layer Protection (Orleans.Rpc.Server)

- [ ] 5.1 Create `Protection/UserRateLimiter.cs` (per-PlayerId limits)
- [ ] 5.2 Create `Protection/MethodRateLimiter.cs` (per-method limits)
- [ ] 5.3 Integrate with `RpcSecurityContext` to get current user for per-user limits
- [ ] 5.4 Configure role-based tiers (Guest: 100/min, User: 500/min, Server: unlimited)
- [ ] 5.5 Add request queue with max pending requests per connection
- [ ] 5.6 Return `RpcStatus.ResourceExhausted` with RetryAfter metadata
- [ ] 5.7 Add metrics: requests_rate_limited_by_user_total, requests_rate_limited_by_method_total

## 6. Resource Protection (Orleans.Rpc.Server)

- [ ] 6.1 Create `Protection/GrainCreationLimiter.cs` (max grain activations per client)
- [ ] 6.2 Add memory pressure detection (GC notifications)
- [ ] 6.3 Add circuit breaker that rejects new requests under extreme memory pressure
- [ ] 6.4 Add per-request timeout enforcement (cancel long-running requests)
- [ ] 6.5 Add metrics: grain_creation_limited_total, memory_pressure_rejections_total

## 7. Configuration and DI

- [ ] 7.1 Create `DDoSProtectionOptions.cs` with all configurable limits
- [ ] 7.2 Create `AddDDoSProtection()` extension method
- [ ] 7.3 Create `AddDDoSProtectionDevelopment()` with permissive defaults
- [ ] 7.4 Create `AddDDoSProtectionProduction()` with strict defaults
- [ ] 7.5 Support per-method limit overrides via attributes or configuration

## 8. Shooter Sample Integration

- [ ] 8.1 Wire up `AddDDoSProtection()` in ActionServer startup
- [ ] 8.2 Configure appropriate limits for game traffic patterns
- [ ] 8.3 Test rate limiting doesn't affect normal gameplay
- [ ] 8.4 Test protection under simulated load

## 9. Testing

- [ ] 9.1 Unit tests for `SlidingWindowRateLimiter` (acquire, exhaust, refill)
- [ ] 9.2 Unit tests for `InMemoryIpBlocklist` (add, check, expire)
- [ ] 9.3 Integration test: connection flood blocked at transport layer
- [ ] 9.4 Integration test: message flood blocked at packet layer
- [ ] 9.5 Integration test: request flood returns ResourceExhausted
- [ ] 9.6 Load test: legitimate traffic unaffected at 80% of limits

## 10. Documentation

- [ ] 10.1 Document DDoS protection configuration options
- [ ] 10.2 Add operational runbook for handling attacks
- [ ] 10.3 Update SECURITY-RECAP.md to mark Phases 8-9 complete
