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

### Decision 1: Type Whitelist with Assembly Scoping

**What**: Allow types from trusted assemblies (application code) by default; block types from untrusted sources.

**Why**: Application types marked with `[GenerateSerializer]` are intentionally serializable. System types (like `System.Diagnostics.Process`) should never be deserialized from network input.

**Implementation**: Scan assemblies at startup for `[GenerateSerializer]` types, build whitelist. Check during deserialization via Orleans codec hooks.

### Decision 2: Attribute-Based Validation

**What**: Use attributes like `[Required]`, `[Range]` on method parameters.

**Why**: Declarative, easy to read, similar to ASP.NET Core model validation. Validators are cached per-method at startup.

**Alternatives considered**:
- FluentValidation → More complex, external dependency
- Manual validation in handlers → Inconsistent, error-prone
- Contract-first validation → Requires schema definition, more work

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

- Should we integrate with DataAnnotations validators? → Yes, reuse existing validators
- How to handle generic types? → Whitelist open generic + verify type arguments
