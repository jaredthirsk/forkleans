# Granville RPC Input Validation Framework Plan

**Document Version**: 1.0
**Created**: 2025-11-30
**Status**: Planning
**Priority**: HIGH (Prevents Invalid Game State)

## Executive Summary

This document specifies systematic validation of RPC method parameters to prevent invalid game state, injection attacks, and resource exhaustion. Validation occurs AFTER deserialization (type safety) but BEFORE business logic execution.

### Current State

- **Challenge**: No systematic parameter validation framework
- **Risk Level**: MEDIUM - Invalid coordinates, negative health, nonsensical data can corrupt game state
- **Attack Surface**: All RPC handlers accept untrusted parameters
- **Existing Mitigations**: Manual validation in handler code (inconsistent, scattered)

### Target State

- Declarative `[Validate]` attributes on RPC methods
- Built-in validators: `[Required]`, `[StringLength]`, `[Range]`, `[ValidateObject]`
- Custom validator support via `IValidator<T>`
- Validation errors returned as `RpcStatus.InvalidArgument`
- Validation logging for suspicious patterns
- Zero validation overhead for valid requests (fast path)

---

## Table of Contents

1. [Validation Architecture](#1-validation-architecture)
2. [Built-in Validators](#2-built-in-validators)
3. [Custom Validators](#3-custom-validators)
4. [Integration Points](#4-integration-points)
5. [Implementation](#5-implementation)
6. [Game-Specific Validators](#6-game-specific-validators)
7. [Testing Strategy](#7-testing-strategy)
8. [Rollout Plan](#8-rollout-plan)

---

## 1. Validation Architecture

### 1.1 Validation Flow Diagram

```
RPC Request Arrives
  ↓
Deserialization (Type Safety) ✓ COVERED by DESERIALIZATION-SAFETY-PLAN
  ├─ Orleans TypeCodec enforces type whitelist
  └─ Resource limits prevent memory exhaustion
  ↓
RPC Handler Invocation
  ├─ RpcValidationFilter intercepts
  │  ├─ Scan method for [Validate] attributes
  │  ├─ Collect all parameter validators
  │  └─ Execute validators in order
  │      ├─ [Required] - non-null check
  │      ├─ [StringLength] - length bounds
  │      ├─ [Range] - numeric bounds
  │      ├─ [ValidateObject] - nested object validation
  │      └─ Custom IValidator<T> implementations
  │
  ├─ If validation passes:
  │  ├─ Log: "VALIDATION_SUCCESS handler={Handler} parameters_valid"
  │  └─ Continue to handler execution
  │
  └─ If validation fails:
     ├─ Log: "VALIDATION_FAILED handler={Handler} reason={Reason}"
     ├─ Log: "SUSPICIOUS_ACTIVITY PlayerId={PlayerId} pattern=invalid_parameters"
     └─ Return: RpcResponse with RpcStatus.InvalidArgument + error details
  ↓
Handler Execution (Business Logic)
  ├─ Guaranteed: All parameters are within valid ranges
  ├─ Guaranteed: No null values for [Required] fields
  └─ Guaranteed: Complex objects are recursively validated
  ↓
Response Serialization & Return
```

### 1.2 Validation Interfaces

```csharp
/// <summary>
/// Base interface for all validators.
/// </summary>
public interface IValidator
{
    /// <summary>
    /// Validate a value.
    /// </summary>
    ValidationResult Validate(object? value, ValidationContext context);
}

/// <summary>
/// Generic validator for type-safe validation.
/// </summary>
public interface IValidator<T> : IValidator
{
    /// <summary>
    /// Validate a strongly-typed value.
    /// </summary>
    new ValidationResult Validate(T? value, ValidationContext context);
}

/// <summary>
/// Result of validation.
/// </summary>
public record ValidationResult
{
    /// <summary>
    /// Is the value valid?
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Human-readable error message if invalid.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Error code for client-side handling.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Additional metadata (e.g., { "min": 0, "max": 100 } for Range).
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Create a success result.
    /// </summary>
    public static ValidationResult Success() =>
        new ValidationResult { IsValid = true };

    /// <summary>
    /// Create a failure result.
    /// </summary>
    public static ValidationResult Failure(string message, string? code = null) =>
        new ValidationResult
        {
            IsValid = false,
            ErrorMessage = message,
            ErrorCode = code ?? "VALIDATION_FAILED"
        };
}

/// <summary>
/// Context for validation (metadata about what's being validated).
/// </summary>
public class ValidationContext
{
    /// <summary>
    /// Name of the parameter/property being validated.
    /// </summary>
    public string PropertyName { get; set; } = string.Empty;

    /// <summary>
    /// RPC handler name (for logging).
    /// </summary>
    public string HandlerName { get; set; } = string.Empty;

    /// <summary>
    /// Security context (user identity).
    /// </summary>
    public RpcSecurityContext? SecurityContext { get; set; }

    /// <summary>
    /// Request ID for tracing.
    /// </summary>
    public string RequestId { get; set; } = string.Empty;
}
```

---

## 2. Built-in Validators (System.ComponentModel.DataAnnotations)

**Decision**: Use `System.ComponentModel.DataAnnotations` as the primary validation API. This provides:
- Familiarity for all .NET developers
- Extensive ecosystem of existing validators
- IDE support and tooling integration
- No custom attributes to learn

### 2.1 [Required] Attribute

Use `System.ComponentModel.DataAnnotations.RequiredAttribute`:

```csharp
using System.ComponentModel.DataAnnotations;

// Usage - familiar to any .NET developer:
Task MovePlayer(
    [Required] string playerId,      // Must be non-null, non-empty
    float x,
    float y);

// On DTOs:
[GenerateSerializer]
public record PlayerMoveRequest
{
    [Id(0)]
    [Required(ErrorMessage = "Player ID is required")]
    public string PlayerId { get; init; } = string.Empty;

    [Id(1)]
    public float X { get; init; }

    [Id(2)]
    public float Y { get; init; }
}
```

### 2.2 [StringLength] Attribute

Use `System.ComponentModel.DataAnnotations.StringLengthAttribute`:

```csharp
using System.ComponentModel.DataAnnotations;

// Usage:
Task SetPlayerName(
    [Required] string playerId,
    [StringLength(32, MinimumLength = 1)] string newName);

// On DTOs:
[GenerateSerializer]
public record SetPlayerNameRequest
{
    [Id(0)]
    [Required]
    public string PlayerId { get; init; } = string.Empty;

    [Id(1)]
    [Required]
    [StringLength(32, MinimumLength = 1, ErrorMessage = "Name must be 1-32 characters")]
    public string NewName { get; init; } = string.Empty;
}

// Constraints:
// - newName must be 1-32 characters
// - Protects against extremely long player names (256KB+ exploit)
// - Prevents memory exhaustion in game UI rendering
```

### 2.3 [Range] Attribute

Use `System.ComponentModel.DataAnnotations.RangeAttribute`:

```csharp
using System.ComponentModel.DataAnnotations;

// Usage - Game Coordinates:
Task MovePlayer(
    [Required] string playerId,
    [Range(-10000.0, 10000.0)] float x,    // World coordinates: -10km to +10km
    [Range(-10000.0, 10000.0)] float y,
    [Range(-1000.0, 1000.0)] float z);     // Vertical: -1km to +1km

// Usage - Game State:
Task TakeDamage(
    [Required] string targetPlayerId,
    [Required] string sourcePlayerId,
    [Range(1, 1000)] int damageAmount);    // 1-1000 damage per hit

// Usage - Game Resources:
Task SpendCurrency(
    [Required] string playerId,
    [Range(1, 1_000_000)] int goldAmount); // Max 1M gold per transaction

// On DTOs:
[GenerateSerializer]
public record TakeDamageRequest
{
    [Id(0)]
    [Required]
    public string TargetPlayerId { get; init; } = string.Empty;

    [Id(1)]
    [Required]
    public string SourcePlayerId { get; init; } = string.Empty;

    [Id(2)]
    [Range(1, 1000, ErrorMessage = "Damage must be between 1 and 1000")]
    public int DamageAmount { get; init; }
}
```

### 2.4 Nested Object Validation

DataAnnotations supports recursive validation via `Validator.TryValidateObject()` with `validateAllProperties: true`. For complex nested objects, the `RpcValidationFilter` recursively validates:

```csharp
using System.ComponentModel.DataAnnotations;

// Usage - Game Character State:
[GenerateSerializer]
public record UpdateCharacterRequest
{
    [Id(0)] [Required] public string PlayerId { get; init; }
    [Id(1)] [ValidateObject] public CharacterStats Stats { get; init; }
    [Id(2)] [ValidateObject] public InventoryUpdate Inventory { get; init; }
}

[GenerateSerializer]
public record CharacterStats
{
    [Id(0)] [Range(0, 10000)] public int Health { get; init; }
    [Id(1)] [Range(0, 10000)] public int Mana { get; init; }
    [Id(2)] [Range(0, 100)] public int Level { get; init; }
}

[GenerateSerializer]
public record InventoryUpdate
{
    [Id(0)] [Range(0, 1000)] public int ItemId { get; init; }
    [Id(1)] [Range(1, 999)] public int Quantity { get; init; }
}

// Validation flow:
// UpdateCharacterRequest
// ├─ PlayerId: [Required] ✓
// ├─ Stats: [ValidateObject]
// │  ├─ Health: [Range(0, 10000)] ✓
// │  ├─ Mana: [Range(0, 10000)] ✓
// │  └─ Level: [Range(0, 100)] ✓
// └─ Inventory: [ValidateObject]
//    ├─ ItemId: [Range(0, 1000)] ✓
//    └─ Quantity: [Range(1, 999)] ✓
```

---

## 3. Custom Validators

### 3.1 IValidator<T> Implementation

For complex validation logic that doesn't fit built-in validators:

```csharp
/// <summary>
/// Custom validator for game-specific coordinates.
/// Ensures player isn't trying to teleport to inaccessible zones.
/// </summary>
public class PlayerCoordinateValidator : IValidator<(float X, float Y)>
{
    private readonly IZoneManager _zoneManager;

    public PlayerCoordinateValidator(IZoneManager zoneManager)
    {
        _zoneManager = zoneManager;
    }

    public ValidationResult Validate(
        (float X, float Y) value,
        ValidationContext context)
    {
        var (x, y) = value;

        // Check if coordinates are on valid terrain
        var zone = _zoneManager.GetZone(x, y);

        if (zone == null || !zone.IsAccessible)
        {
            return ValidationResult.Failure(
                $"Coordinates ({x}, {y}) are not on valid terrain",
                "INVALID_TERRAIN",
                new()
                {
                    { "x", x },
                    { "y", y },
                    { "zone", zone?.Name ?? "unknown" }
                });
        }

        // Check if player is trying to teleport too far (speed hack detection)
        var lastKnownPosition = _zoneManager.GetPlayerLastPosition(context.SecurityContext?.PlayerId);

        if (lastKnownPosition.HasValue)
        {
            var distance = Math.Sqrt(
                Math.Pow(x - lastKnownPosition.Value.X, 2) +
                Math.Pow(y - lastKnownPosition.Value.Y, 2));

            var maxDistance = 100; // Max 100 units per move (tuned for game speed)

            if (distance > maxDistance)
            {
                return ValidationResult.Failure(
                    $"Movement of {distance:F1} units exceeds maximum {maxDistance}",
                    "TELEPORT_DETECTED",
                    new()
                    {
                        { "distance", distance },
                        { "max_allowed", maxDistance }
                    });
            }
        }

        return ValidationResult.Success();
    }
}

/// <summary>
/// Attribute to trigger custom validator.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public class ValidateCoordinateAttribute : ValidationAttribute
{
    private IPlayerCoordinateValidator? _validator;

    public override ValidationResult Validate(object? value)
    {
        _validator ??= ServiceProvider.GetRequiredService<IPlayerCoordinateValidator>();

        if (value is not (float, float) coord)
        {
            return ValidationResult.Failure(
                "Coordinates must be a tuple of (float, float)",
                "INVALID_TYPE");
        }

        return _validator.Validate(coord, new ValidationContext());
    }
}

// Usage:
Task MovePlayer(
    [Required] string playerId,
    [ValidateCoordinate] (float X, float Y) newPosition);
```

---

## 4. Integration Points

### 4.1 RpcValidationFilter

Intercepts RPC method invocation and performs validation:

```csharp
/// <summary>
/// RPC filter that validates all method parameters before invocation.
/// </summary>
public class RpcValidationFilter : IRpcInvokeFilter
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;
    private readonly IRpcSecurityEventLogger _securityLogger;

    public async Task OnInvokeAsync(
        IInvokeContext context,
        Func<IInvokeContext, Task> next)
    {
        var method = context.Method;

        // Check if validation is enabled on this method
        var validateAttr = method.GetCustomAttribute<ValidateAttribute>();

        if (validateAttr == null)
        {
            // No validation requested, proceed
            await next(context);
            return;
        }

        // Collect all parameter validators
        var validationErrors = new List<(string ParamName, ValidationResult Result)>();

        var parameters = method.GetParameters();

        for (int i = 0; i < parameters.Length; i++)
        {
            var param = parameters[i];
            var value = context.Arguments[i];

            // Find all validators for this parameter
            var validators = param.GetCustomAttributes<ValidationAttribute>();

            foreach (var validator in validators)
            {
                var ctx = new ValidationContext
                {
                    PropertyName = param.Name,
                    HandlerName = method.Name,
                    SecurityContext = RpcSecurityContext.Current,
                    RequestId = RpcSecurityContext.Current?.RequestId ?? "unknown"
                };

                var result = validator.Validate(value, ctx);

                if (!result.IsValid)
                {
                    validationErrors.Add((param.Name, result));

                    _logger.LogWarning(
                        "[VALIDATION] Parameter validation failed: " +
                        "Handler={Handler} Param={ParamName} Error={Error} RequestId={RequestId}",
                        method.Name, param.Name, result.ErrorMessage,
                        ctx.RequestId);
                }
            }
        }

        // If validation failed, return error response
        if (validationErrors.Count > 0)
        {
            _securityLogger.LogValidationFailure(
                method.Name,
                RpcSecurityContext.Current?.PlayerId ?? "unknown",
                validationErrors);

            // Return validation error
            var response = new RpcResponse
            {
                Status = RpcStatus.InvalidArgument,
                Error = new RpcError
                {
                    Code = "VALIDATION_FAILED",
                    Message = "One or more parameters are invalid",
                    Details = validationErrors
                        .ToDictionary(
                            x => x.ParamName,
                            x => (object?)x.Result.ErrorMessage)
                }
            };

            context.Result = response;
            return;
        }

        // All validations passed, proceed to handler
        await next(context);
    }
}

/// <summary>
/// Enable validation on RPC method.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class ValidateAttribute : Attribute
{
}
```

### 4.2 Integration with DI

```csharp
// In RPC builder configuration
builder.UseRpc(rpc =>
{
    rpc
        .UseLiteNetLib()
        // ... other config ...
        .AddInvokeFilter<RpcValidationFilter>()  // Add validation filter
        .ConfigureValidation(validation =>
        {
            // Register custom validators
            validation.RegisterValidator(typeof(PlayerCoordinateValidator));
            validation.RegisterValidator(typeof(WeaponCooldownValidator));
            validation.RegisterValidator(typeof(InventoryValidator));
        });
});
```

---

## 5. Implementation

### 5.1 Validation Strategy

**Primary validation**: Use `System.ComponentModel.DataAnnotations` for standard validation (Required, Range, StringLength, etc.)

**Custom validation**: Use `IRpcValidator<T>` interface for game-specific logic (speed-hack detection, coordinate validation, etc.)

### 5.2 DataAnnotations Integration

**File**: `/src/Rpc/Orleans.Rpc.Server/Filters/RpcValidationFilter.cs` (NEW)

```csharp
using System.ComponentModel.DataAnnotations;

/// <summary>
/// RPC filter that validates parameters using DataAnnotations.
/// </summary>
public class RpcValidationFilter : IRpcInvokeFilter
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RpcValidationFilter> _logger;

    public async Task OnInvokeAsync(IInvokeContext context, Func<IInvokeContext, Task> next)
    {
        var method = context.Method;
        var validateAttr = method.GetCustomAttribute<ValidateAttribute>();

        if (validateAttr == null)
        {
            await next(context);
            return;
        }

        var errors = new List<ValidationResult>();

        // Validate each parameter using DataAnnotations
        var parameters = method.GetParameters();
        for (int i = 0; i < parameters.Length; i++)
        {
            var param = parameters[i];
            var value = context.Arguments[i];

            // For complex objects, use Validator.TryValidateObject
            if (value != null && !param.ParameterType.IsPrimitive && param.ParameterType != typeof(string))
            {
                var validationContext = new ValidationContext(value, _serviceProvider, null);
                Validator.TryValidateObject(value, validationContext, errors, validateAllProperties: true);
            }

            // Validate parameter-level attributes
            var paramAttrs = param.GetCustomAttributes<ValidationAttribute>();
            foreach (var attr in paramAttrs)
            {
                var result = attr.GetValidationResult(value, new ValidationContext(value ?? new object())
                {
                    MemberName = param.Name
                });

                if (result != ValidationResult.Success && result != null)
                {
                    errors.Add(result);
                }
            }
        }

        if (errors.Count > 0)
        {
            _logger.LogWarning(
                "[VALIDATION] Failed for {Method}: {Errors}",
                method.Name,
                string.Join(", ", errors.Select(e => e.ErrorMessage)));

            context.Result = new RpcResponse
            {
                Status = RpcStatus.InvalidArgument,
                Error = new RpcError
                {
                    Code = "VALIDATION_FAILED",
                    Message = "One or more parameters are invalid",
                    Details = errors.ToDictionary(
                        e => e.MemberNames.FirstOrDefault() ?? "unknown",
                        e => (object?)e.ErrorMessage)
                }
            };
            return;
        }

        await next(context);
    }
}
```

### 5.3 Custom Validator Interface (for Game-Specific Logic)

**File**: `/src/Rpc/Orleans.Rpc.Security/Validation/IRpcValidator.cs` (NEW)

```csharp
namespace Granville.Rpc.Security.Validation;

/// <summary>
/// Custom validator for RPC-specific validation (speed-hack detection, etc.).
/// Use System.ComponentModel.DataAnnotations for standard validation.
/// </summary>
public interface IRpcValidator<T>
{
    /// <summary>
    /// Validate a value with RPC context (security context, request ID, etc.).
    /// </summary>
    RpcValidationResult Validate(T? value, RpcValidationContext context);
}

/// <summary>
/// Result from custom RPC validation.
/// </summary>
public record RpcValidationResult(bool IsValid, string? ErrorMessage = null, string? ErrorCode = null)
{
    public static RpcValidationResult Success() => new(true);
    public static RpcValidationResult Failure(string message, string? code = null) => new(false, message, code);
}

/// <summary>
/// Context for RPC validation (includes security context).
/// </summary>
public record RpcValidationContext(
    string PropertyName,
    string HandlerName,
    RpcSecurityContext? SecurityContext,
    string RequestId);
```

---

## 6. Game-Specific Validators

These validators use `IRpcValidator<T>` for game logic that requires RPC context (security context, player state, etc.). For simple range/required checks, use `System.ComponentModel.DataAnnotations` attributes instead.

### 6.1 Movement Validation (Speed-Hack Detection)

```csharp
/// <summary>
/// Validates player movement for speed hacks and impossible teleportation.
/// Uses IRpcValidator because it needs game state and security context.
/// </summary>
public class MovementValidator : IRpcValidator<(float X, float Y, float Z)>
{
    private readonly IZoneManager _zoneManager;
    private readonly IGameStateCache _gameState;
    private readonly ILogger _logger;

    public RpcValidationResult Validate(
        (float X, float Y, float Z)? position,
        RpcValidationContext context)
    {
        if (position == null) return RpcValidationResult.Success();

        var (x, y, z) = position.Value;

        // 1. Check bounds (could also use [Range] attribute on DTO)
        if (x < -10000 || x > 10000 || y < -10000 || y > 10000)
        {
            return RpcValidationResult.Failure(
                $"Coordinates out of world bounds: ({x}, {y})",
                "OUT_OF_BOUNDS");
        }

        // 2. Check terrain validity
        var zone = _zoneManager.GetZone(x, y);
        if (zone == null || !zone.IsAccessible)
        {
            return RpcValidationResult.Failure(
                $"Coordinates ({x}, {y}) are on inaccessible terrain",
                "INACCESSIBLE_TERRAIN");
        }

        // 3. Check for teleportation (speed hack) - requires security context
        var lastPosition = _gameState.GetPlayerPosition(context.SecurityContext?.PlayerId);

        if (lastPosition.HasValue)
        {
            var distance = Vector3.Distance(
                new Vector3(lastPosition.Value.X, lastPosition.Value.Y, lastPosition.Value.Z),
                new Vector3(x, y, z));

            var maxDistance = 100; // Game-specific: max 100 units per movement
            var elapsed = (DateTime.UtcNow - _gameState.GetLastMovementTime(context.SecurityContext?.PlayerId)).TotalSeconds;
            var maxExpectedDistance = (float)elapsed * 15; // 15 units/sec = 54 km/h max speed

            if (distance > maxExpectedDistance)
            {
                _logger.LogWarning(
                    "[CHEAT_DETECTION] Teleport detected: " +
                    "PlayerId={PlayerId} Distance={Distance}m MaxExpected={MaxExpected}m",
                    context.SecurityContext?.PlayerId,
                    distance,
                    maxExpectedDistance);

                return RpcValidationResult.Failure(
                    $"Movement of {distance:F1}m exceeds expected {maxExpectedDistance:F1}m",
                    "TELEPORT_DETECTED");
            }
        }

        return RpcValidationResult.Success();
    }
}
```

### 6.2 Combat Validation (Fire-Rate Hack Detection)

```csharp
/// <summary>
/// Validates combat actions for unrealistic fire rates and damage.
/// Uses IRpcValidator because it needs game state and security context.
/// </summary>
public class CombatValidator : IRpcValidator<CombatAction>
{
    private readonly IGameStateCache _gameState;

    public RpcValidationResult Validate(
        CombatAction? action,
        RpcValidationContext context)
    {
        if (action == null)
            return RpcValidationResult.Success();

        // 1. Check weapon exists
        var weapon = _gameState.GetWeapon(action.WeaponId);
        if (weapon == null)
        {
            return RpcValidationResult.Failure(
                $"Weapon {action.WeaponId} does not exist",
                "INVALID_WEAPON");
        }

        // 2. Check fire rate (no faster than weapon allows) - requires player state
        var lastAttackTime = _gameState.GetLastAttackTime(context.SecurityContext?.PlayerId, action.WeaponId);

        if (lastAttackTime.HasValue)
        {
            var elapsed = (DateTime.UtcNow - lastAttackTime.Value).TotalSeconds;
            var minCooldown = weapon.FireRateSeconds;

            if (elapsed < minCooldown)
            {
                return RpcValidationResult.Failure(
                    $"Weapon is on cooldown for {minCooldown - elapsed:F2}s",
                    "WEAPON_COOLDOWN");
            }
        }

        // 3. Check damage is within weapon limits
        // Note: This could also be done with [Range] on the DTO
        if (action.DamageAmount < weapon.MinDamage || action.DamageAmount > weapon.MaxDamage)
        {
            return RpcValidationResult.Failure(
                $"Damage {action.DamageAmount} outside weapon range [{weapon.MinDamage}, {weapon.MaxDamage}]",
                "INVALID_DAMAGE");
        }

        return RpcValidationResult.Success();
    }
}
```

---

## 7. Testing Strategy

### 7.1 Unit Tests

**File**: `/granville/test/Rpc.Security.Tests/ValidationTests.cs` (NEW)

```csharp
using System.ComponentModel.DataAnnotations;

public class DataAnnotationsValidationTests
{
    [Fact]
    public void RequiredValidator_RejectsNull()
    {
        // Arrange - Use standard DataAnnotations
        var dto = new PlayerMoveRequest { PlayerId = null!, X = 0, Y = 0 };
        var results = new List<ValidationResult>();
        var context = new ValidationContext(dto);

        // Act
        var isValid = Validator.TryValidateObject(dto, context, results, validateAllProperties: true);

        // Assert
        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains("PlayerId"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(1000)]
    public void RangeValidator_RejectsOutOfRange(int value)
    {
        // Arrange
        var dto = new TakeDamageRequest
        {
            TargetPlayerId = "target",
            SourcePlayerId = "source",
            DamageAmount = value  // Out of range [1, 100]
        };
        var results = new List<ValidationResult>();
        var context = new ValidationContext(dto);

        // Act
        var isValid = Validator.TryValidateObject(dto, context, results, validateAllProperties: true);

        // Assert - value of 1 is valid, others are not
        if (value >= 1 && value <= 100)
            Assert.True(isValid);
        else
            Assert.False(isValid);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(100)]
    public void RangeValidator_AcceptsInRange(int value)
    {
        // Arrange
        var dto = new TakeDamageRequest
        {
            TargetPlayerId = "target",
            SourcePlayerId = "source",
            DamageAmount = value
        };
        var results = new List<ValidationResult>();
        var context = new ValidationContext(dto);

        // Act
        var isValid = Validator.TryValidateObject(dto, context, results, validateAllProperties: true);

        // Assert
        Assert.True(isValid);
    }
}

// Test DTOs with DataAnnotations
[GenerateSerializer]
public record TakeDamageRequest
{
    [Id(0)] [Required] public string TargetPlayerId { get; init; } = string.Empty;
    [Id(1)] [Required] public string SourcePlayerId { get; init; } = string.Empty;
    [Id(2)] [Range(1, 100)] public int DamageAmount { get; init; }
}
```

### 7.2 Integration Tests

Test validation filter intercepts and blocks invalid requests:

```csharp
public class RpcValidationIntegrationTests
{
    [Fact]
    public async Task InvalidParameter_ReturnsValidationError()
    {
        // Arrange
        var request = new MovePlayerRequest
        {
            PlayerId = "player1",
            X = 20000,  // Out of bounds (> 10000)
            Y = 5000
        };

        // Act
        var response = await _rpcClient.InvokeAsync("MovePlayer", request);

        // Assert
        Assert.Equal(RpcStatus.InvalidArgument, response.Status);
        Assert.Equal("VALIDATION_FAILED", response.Error?.Code);
    }
}
```

---

## 8. Rollout Plan

### Phase 1: Core Validation Infrastructure (Week 1)
- [ ] Implement IValidator<T> interface
- [ ] Implement ValidationResult and ValidationContext
- [ ] Implement base ValidationAttribute class
- [ ] Unit tests for infrastructure

### Phase 2: Built-in Validators (Week 1-2)
- [ ] Implement [Required]
- [ ] Implement [StringLength]
- [ ] Implement [Range]
- [ ] Unit tests for each validator

### Phase 3: RPC Integration (Week 2)
- [ ] Implement RpcValidationFilter
- [ ] Integrate with RPC handler invocation
- [ ] Handle validation errors in response
- [ ] Integration tests

### Phase 4: Game-Specific Validators (Week 3)
- [ ] Implement MovementValidator
- [ ] Implement CombatValidator
- [ ] Implement InventoryValidator
- [ ] Security tests for cheat detection

### Phase 5: Documentation & Rollout (Week 3-4)
- [ ] Developer guide for using validators
- [ ] Best practices for game parameter ranges
- [ ] Performance benchmarking
- [ ] Deploy to staging, then production

---

## Summary

**Input Validation Framework** provides:
- ✅ Declarative validation using attributes
- ✅ Built-in validators for common cases
- ✅ Custom validator support for game logic
- ✅ Zero overhead for valid requests
- ✅ Detailed validation errors for client
- ✅ Security logging of validation failures
- ✅ Cheat detection integration (speed hacks, fire rate exploits)

**Dependencies**: Requires DESERIALIZATION-SAFETY-PLAN (type safety), AUTHORIZATION-FILTER-PLAN (context), SECURITY-LOGGING-PLAN (event logging)
