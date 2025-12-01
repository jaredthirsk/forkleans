# Change: Add Input Security (Deserialization Safety + Validation)

## Why

Granville RPC accepts untrusted serialized data from the network. Without controls:
1. **Deserialization attacks** (CWE-502): Attackers can instantiate arbitrary types, potentially achieving Remote Code Execution via gadget chains
2. **Invalid input**: Malformed parameters (negative health, impossible coordinates) can corrupt game state

Both are rated **HIGH** risk in the threat model and must be addressed before internet deployment.

## What Changes

### Phase 10: Deserialization Safety

- **Type whitelisting**: Only explicitly allowed types can be deserialized
- **IRpcTypePolicy**: Interface for type allow/deny decisions
- **TypeWhitelist**: Registry of allowed types (auto-populated from `[GenerateSerializer]` types)
- **Resource limits**: Max object depth (100), max collection size (10,000), max string length (1MB)
- **Deny-by-default**: Unknown types blocked with security logging

### Phase 11: Input Validation Framework

- **`[Validate]` attribute**: Enable validation on RPC methods
- **Built-in validators**: `[Required]`, `[StringLength]`, `[Range]`, `[ValidateObject]`
- **`IValidator<T>` interface**: Custom validator support
- **Validation errors**: Return `RpcStatus.InvalidArgument` with details
- **Game-specific validators**: Coordinate bounds, velocity limits, etc.

## Impact

- **Affected specs**: New `rpc-input-security` capability
- **Affected code**:
  - `/src/Rpc/Orleans.Rpc.Abstractions/Validation/` - Validation attributes
  - `/src/Rpc/Orleans.Rpc.Security/TypeSafety/` - Type whitelisting
  - `/src/Rpc/Orleans.Rpc.Security/Validation/` - Validator implementations
  - Orleans serialization integration points
- **Dependencies**: None (can be implemented independently)
- **Breaking changes**: May reject previously-accepted types if not whitelisted

## References

- Deserialization plan: `/src/Rpc/docs/security/roadmap/DESERIALIZATION-SAFETY-PLAN.md`
- Input validation plan: `/src/Rpc/docs/security/roadmap/INPUT-VALIDATION-PLAN.md`
- Security roadmap Phases 10-11: `/src/Rpc/docs/security/roadmap/SECURITY-RECAP.md`
