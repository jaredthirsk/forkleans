## Context

RPC systems accepting untrusted input face two categories of attacks: type-level (deserializing malicious types) and value-level (valid types with invalid values). This design addresses both with separate but complementary mechanisms.

**Stakeholders**: Security auditors, game developers, operations teams

**Constraints**:
- Must integrate with Orleans serialization without forking Orleans code
- Must not break existing applications when enabled with proper whitelists
- Validation overhead must be minimal for valid requests
- Must support gradual rollout (audit mode before enforcement)

## Goals / Non-Goals

**Goals**:
- Prevent arbitrary type instantiation (RCE protection)
- Validate all RPC parameters against defined constraints
- Provide clear error messages for debugging
- Support both development (audit) and production (enforce) modes
- Auto-discover safe types from `[GenerateSerializer]` attributes

**Non-Goals**:
- Modifying Orleans serialization internals
- Supporting arbitrary .NET serialization (only Orleans serialization)
- Business rule validation (handled in grain logic)

## Decisions

### Decision 1: Type Whitelist with Assembly Scoping (Defense in Depth)

**What**: Allow types from trusted assemblies (application code) by default; block types from untrusted sources.

**Why**: Application types marked with `[GenerateSerializer]` are intentionally serializable. System types (like `System.Diagnostics.Process`) should never be deserialized from network input.

**Implementation**: Scan assemblies at startup for `[GenerateSerializer]` types, build whitelist. Check during deserialization via Orleans codec hooks.

**Note on Orleans Codecs**: Orleans only generates codecs for types with `[GenerateSerializer]`, so types without codecs will fail deserialization anyway. However, explicit whitelisting remains valuable as **defense in depth**:
- **Auditability**: Explicit list of allowed types for security review
- **Logging**: Detect and log attempted attacks even if they would fail
- **Edge cases**: Orleans has fallback serializers for some built-in types
- **Future-proofing**: Protection against Orleans serialization changes
- **ISerializable types**: Some configurations support these (potential gadget vectors)

Type whitelisting should be **optional but recommended** for internet-facing deployments.

### Decision 2: Use System.ComponentModel.DataAnnotations

**What**: Use `System.ComponentModel.DataAnnotations` attributes (`[Required]`, `[Range]`, `[StringLength]`, etc.) on RPC method parameters and DTO properties.

**Why**:
- Familiar to all .NET developers (used in ASP.NET Core, EF Core, Blazor)
- Extensive ecosystem of existing validators
- Works with existing tooling and IDE support
- No new attributes to learn

**Implementation**: `RpcValidationFilter` invokes DataAnnotations `Validator.TryValidateObject()` and converts results to `RpcStatus.InvalidArgument` responses.

**Custom validators**: For RPC-specific validation (e.g., speed-hack detection, game coordinate bounds), use `IValidator<T>` interface alongside DataAnnotations.

**Alternatives considered**:
- FluentValidation → More complex, external dependency
- Custom Granville attributes → Duplicates existing functionality, learning curve
- Manual validation in handlers → Inconsistent, error-prone

### Decision 3: Validation Filter Pipeline

**What**: Run validation as a filter before method invocation (after deserialization, before grain logic).

**Why**: Consistent location in request pipeline. Validation failures short-circuit before expensive grain operations.

### Decision 4: Audit Mode for Gradual Rollout

**What**: Support "audit" mode that logs violations but allows requests through.

**Why**: Allows deploying type whitelisting without immediately breaking unknown edge cases. Review logs, add to whitelist, then enable enforcement.

## Risks / Trade-offs

| Risk | Mitigation |
|------|------------|
| Missing types from whitelist breaks features | Audit mode logs missing types; easy to add |
| Performance overhead from validation | Cache validators per-method; skip when no `[Validate]` |
| False positives reject valid requests | Clear error messages; validation tests in CI |
| Orleans serialization hooks may not exist | Use codec registration; worst case: custom serializer wrapper |

## Migration Plan

1. **Phase 1**: Add type policy infrastructure (audit mode only)
2. **Phase 2**: Auto-populate whitelist from `[GenerateSerializer]` types
3. **Phase 3**: Enable enforcement in non-production environments
4. **Phase 4**: Add validation attributes and validators
5. **Phase 5**: Enable in production with monitoring
6. **Rollback**: Configuration to disable enforcement per feature

## Open Questions

- ~~Should we integrate with DataAnnotations validators?~~ → **Resolved**: Yes, use `System.ComponentModel.DataAnnotations` as the primary validation API (see Decision 2)
- How to handle generic types? → Whitelist open generic + verify type arguments
- ~~Is type whitelisting necessary given Orleans codec requirements?~~ → **Resolved**: Optional but recommended as defense-in-depth (see Decision 1 note)
