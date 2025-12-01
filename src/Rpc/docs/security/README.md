# Granville RPC Security Documentation

Welcome to the security documentation for Granville RPC. This index helps you find the right document for your needs.

## Status

**Current Security Level**: Transport encryption implemented, application-layer security in progress.

| Component | Status | Notes |
|-----------|--------|-------|
| Transport Encryption (PSK) | **Implemented** | AES-256-GCM with pre-shared keys |
| Session Management | **Implemented** | Orleans grains store session keys |
| HTTP Authentication | **Implemented** | Guest mode, returns session key |
| `UseNoSecurity()` | **Implemented** | For development only |
| `UsePskEncryption()` | **Implemented** | For production use |
| Authorization | Not Started | Phases 4-7 of roadmap |
| Rate Limiting | Not Started | Phases 8-9 of roadmap |
| Input Validation | Not Started | Phases 10-11 of roadmap |

**Overall Progress**: ~20% (3/15 phases complete)

See [roadmap/SECURITY-RECAP.md](roadmap/SECURITY-RECAP.md) for detailed progress tracking.

---

## Guides

Practical how-to documentation for implementing security features.

| Guide | Description | Audience |
|-------|-------------|----------|
| [PSK-SECURITY-HOWTO.md](PSK-SECURITY-HOWTO.md) | **Start here!** How to use PSK encryption and UseNoSecurity | Developers |
| [IMPLEMENTATION-GUIDE.md](IMPLEMENTATION-GUIDE.md) | Future/aspirational security APIs (not yet implemented) | Architects |
| [SECURITY-SERIALIZATION-GUIDE.md](SECURITY-SERIALIZATION-GUIDE.md) | Safe deserialization practices | Developers |

### Quick Start

**For development** (local testing only):
```csharp
using Granville.Rpc.Security;

rpcBuilder.UseLiteNetLib();
rpcBuilder.UseNoSecurity();  // Logs warning at startup
```

**For production**:
```csharp
using Granville.Rpc.Security;

rpcBuilder.UseLiteNetLib();
rpcBuilder.UsePskEncryption(options =>
{
    options.IsServer = true;
    options.PskLookup = async (playerId, ct) =>
    {
        var grain = grainFactory.GetGrain<IPlayerSessionGrain>(playerId);
        var session = await grain.GetSessionAsync();
        return session?.GetSessionKeyBytes();
    };
});
```

See [PSK-SECURITY-HOWTO.md](PSK-SECURITY-HOWTO.md) for complete examples.

---

## Reference

In-depth technical documentation and design documents.

| Document | Description |
|----------|-------------|
| [THREAT-MODEL.md](THREAT-MODEL.md) | Security risk analysis and threat categories |
| [SECURITY-CONCERNS.md](SECURITY-CONCERNS.md) | Catalog of potential vulnerabilities |
| [AUTHENTICATION-DESIGN.md](AUTHENTICATION-DESIGN.md) | Authentication architecture design |
| [AUTHORIZATION-DESIGN.md](AUTHORIZATION-DESIGN.md) | Role-based access control design |

### Key Security Concepts

- **PSK (Pre-Shared Key)**: Session key generated during HTTP auth, used to encrypt UDP traffic
- **Session Grain**: Orleans grain that stores player session and PSK
- **Transport Decorator**: Security wraps the transport layer (LiteNetLib/Ruffles)
- **Challenge-Response**: Handshake protocol to establish encrypted channel

---

## Roadmap

Planning documents for security implementation.

| Document | Description |
|----------|-------------|
| [roadmap/SECURITY-RECAP.md](roadmap/SECURITY-RECAP.md) | **Master roadmap** - 15-phase implementation plan |
| [roadmap/PSK-ARCHITECTURE-PLAN.md](roadmap/PSK-ARCHITECTURE-PLAN.md) | Detailed PSK design (Phases 1-3) |
| [roadmap/DESERIALIZATION-SAFETY-PLAN.md](roadmap/DESERIALIZATION-SAFETY-PLAN.md) | Type whitelisting plan |
| [roadmap/DDOS-RESOURCE-EXHAUSTION-PLAN.md](roadmap/DDOS-RESOURCE-EXHAUSTION-PLAN.md) | Rate limiting plan |

### Implementation Phases

| Phase | Description | Status |
|-------|-------------|--------|
| 1 | HTTP Auth & Session Grains | **Complete** |
| 2 | PSK Transport Layer | **Complete** |
| 3 | Security Mode Configuration | **Complete** |
| 4 | RPC Call Context Integration | Not Started |
| 5 | Basic Authorization Attributes | Not Started |
| 6 | Role System and Hierarchy | Not Started |
| 7 | Grain Access Control | Not Started |
| 8 | Rate Limiting (Per-IP) | Not Started |
| 9 | Rate Limiting (Per-User) | Not Started |
| 10 | Type Whitelisting | Not Started |
| 11 | Input Validation Framework | Not Started |
| 12 | Security Event Logging | Not Started |
| 13 | Session Management | Not Started |
| 14 | Anti-Cheat Foundation | Not Started |
| 15 | Production Hardening | Not Started |

---

## Source Code

Key files in the `Granville.Rpc.Security` package:

```
src/Rpc/Orleans.Rpc.Security/
├── Orleans.Rpc.Security.csproj
├── Configuration/
│   └── DtlsPskOptions.cs          # PSK configuration options
├── Transport/
│   ├── PskEncryptedTransport.cs   # Transport decorator
│   ├── PskEncryptedTransportFactory.cs
│   └── PskSession.cs              # AES-GCM encryption
└── Extensions/
    └── SecurityExtensions.cs      # UsePskEncryption, UseNoSecurity
```

---

## Sample Implementation

The Shooter game sample demonstrates PSK security:

```
granville/samples/Rpc/
├── Shooter.Shared/GrainInterfaces/
│   └── IPlayerSessionGrain.cs     # Session grain interface
├── Shooter.Silo/
│   ├── Grains/PlayerSessionGrain.cs  # Session implementation
│   └── Controllers/WorldController.cs # HTTP auth endpoint
├── Shooter.ActionServer/
│   └── Program.cs                 # Server PSK configuration
└── Shooter.Client.Common/
    └── GranvilleRpcGameClientService.cs # Client PSK configuration
```

---

## Getting Help

- **Implementation questions**: Start with [PSK-SECURITY-HOWTO.md](PSK-SECURITY-HOWTO.md)
- **Architecture decisions**: See [roadmap/SECURITY-RECAP.md](roadmap/SECURITY-RECAP.md)
- **Threat analysis**: See [THREAT-MODEL.md](THREAT-MODEL.md)
- **Future features**: See [IMPLEMENTATION-GUIDE.md](IMPLEMENTATION-GUIDE.md) (aspirational APIs)

---

## Document History

| Date | Change |
|------|--------|
| 2024-11-30 | Phases 1-3 complete (PSK transport security) |
| 2024-11-30 | Created PSK-SECURITY-HOWTO.md |
| 2024-11-30 | Created this README index |
