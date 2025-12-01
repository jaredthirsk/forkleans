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

## 2. Built-in Validators

### 2.1 [Required] Attribute

Ensures value is not null and (for strings) not empty.

```csharp
/// <summary>
/// Declares that a parameter is required (non-null).
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public class RequiredAttribute : ValidationAttribute
{
    /// <summary>
    /// Optional custom error message.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Validate that value is not null.
    /// </summary>
    public override ValidationResult Validate(object? value)
    {
        if (value == null)
        {
            return ValidationResult.Failure(
                ErrorMessage ?? "This field is required",
                "REQUIRED_MISSING");
        }

        if (value is string str && string.IsNullOrWhiteSpace(str))
        {
            return ValidationResult.Failure(
                ErrorMessage ?? "This field is required and cannot be empty",
                "REQUIRED_EMPTY");
        }

        return ValidationResult.Success();
    }
}

// Usage:
Task MovePlayer(
    [Required] string playerId,      // Must be non-null, non-empty
    [Required] float x,              // Can't use [Required] on value types
    float y);

// Generated validator:
public class MovePlayerValidator
{
    public bool ValidatePlayerId(string? playerId)
    {
        return !string.IsNullOrWhiteSpace(playerId);
    }
}
```

### 2.2 [StringLength] Attribute

Ensures string length is within bounds.

```csharp
/// <summary>
/// Validates string length bounds.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public class StringLengthAttribute : ValidationAttribute
{
    /// <summary>
    /// Maximum allowed length.
    /// </summary>
    public int MaxLength { get; }

    /// <summary>
    /// Optional minimum length (default: 0).
    /// </summary>
    public int MinLength { get; set; } = 0;

    public StringLengthAttribute(int maxLength)
    {
        MaxLength = maxLength;
    }

    public override ValidationResult Validate(object? value)
    {
        if (value == null) return ValidationResult.Success();

        if (value is not string str)
        {
            return ValidationResult.Failure(
                "StringLength can only validate strings",
                "INVALID_TYPE");
        }

        if (str.Length < MinLength || str.Length > MaxLength)
        {
            return ValidationResult.Failure(
                $"String length must be between {MinLength} and {MaxLength}",
                "STRING_LENGTH_OUT_OF_RANGE",
                new() { { "min", MinLength }, { "max", MaxLength }, { "actual", str.Length } });
        }

        return ValidationResult.Success();
    }
}

// Usage:
Task SetPlayerName(
    [Required] string playerId,
    [StringLength(32, MinLength = 1)] string newName);

// Constraints:
// - newName must be 1-32 characters
// - Protects against extremely long player names (256KB+ exploit)
// - Prevents memory exhaustion in game UI rendering
```

### 2.3 [Range] Attribute

Ensures numeric values are within bounds.

```csharp
/// <summary>
/// Validates numeric value within range.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public class RangeAttribute : ValidationAttribute
{
    /// <summary>
    /// Minimum allowed value.
    /// </summary>
    public IComparable Minimum { get; }

    /// <summary>
    /// Maximum allowed value.
    /// </summary>
    public IComparable Maximum { get; }

    public RangeAttribute(int minimum, int maximum)
        : this((IComparable)minimum, (IComparable)maximum) { }

    public RangeAttribute(double minimum, double maximum)
        : this((IComparable)minimum, (IComparable)maximum) { }

    public RangeAttribute(IComparable minimum, IComparable maximum)
    {
        Minimum = minimum;
        Maximum = maximum;
    }

    public override ValidationResult Validate(object? value)
    {
        if (value == null) return ValidationResult.Success();

        if (value is not IComparable comparable)
        {
            return ValidationResult.Failure(
                "Value must be comparable",
                "NOT_COMPARABLE");
        }

        if (comparable.CompareTo(Minimum) < 0 || comparable.CompareTo(Maximum) > 0)
        {
            return ValidationResult.Failure(
                $"Value must be between {Minimum} and {Maximum}",
                "VALUE_OUT_OF_RANGE",
                new() { { "min", Minimum }, { "max", Maximum }, { "actual", value } });
        }

        return ValidationResult.Success();
    }
}

// Usage - Game Coordinates:
Task MovePlayer(
    [Required] string playerId,
    [Range(-10000, 10000)] float x,        // World coordinates: -10km to +10km
    [Range(-10000, 10000)] float y,
    [Range(-1000, 1000)] float z);         // Vertical: -1km to +1km

// Usage - Game State:
Task TakeDamage(
    [Required] string targetPlayerId,
    [Required] string sourceBuilderId,
    [Range(1, 1000)] int damageAmount);    // 1-1000 damage per hit

// Usage - Game Resources:
Task SpendCurrency(
    [Required] string playerId,
    [Range(1, 1_000_000)] int goldAmount); // Max 1M gold per transaction
```

### 2.4 [ValidateObject] Attribute

Recursively validates nested objects.

```csharp
/// <summary>
/// Validates a complex object by validating its properties.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public class ValidateObjectAttribute : ValidationAttribute
{
    public override ValidationResult Validate(object? value)
    {
        if (value == null) return ValidationResult.Success();

        // Scan value's properties for validation attributes
        var properties = value.GetType().GetProperties();

        foreach (var prop in properties)
        {
            var propValue = prop.GetValue(value);
            var validators = prop.GetCustomAttributes<ValidationAttribute>();

            foreach (var validator in validators)
            {
                var result = validator.Validate(propValue);
                if (!result.IsValid)
                {
                    return ValidationResult.Failure(
                        $"Property '{prop.Name}': {result.ErrorMessage}",
                        result.ErrorCode);
                }
            }
        }

        return ValidationResult.Success();
    }
}

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

### 5.1 Core Validation Interfaces

**File**: `/src/Rpc/Orleans.Rpc.Security/Validation/IValidator.cs` (NEW)

```csharp
using System;

namespace Granville.Rpc.Security.Validation;

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
    public required bool IsValid { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorCode { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }

    public static ValidationResult Success() =>
        new ValidationResult { IsValid = true };

    public static ValidationResult Failure(
        string message,
        string? code = null,
        Dictionary<string, object>? metadata = null) =>
        new ValidationResult
        {
            IsValid = false,
            ErrorMessage = message,
            ErrorCode = code ?? "VALIDATION_FAILED",
            Metadata = metadata
        };
}

/// <summary>
/// Context for validation.
/// </summary>
public class ValidationContext
{
    public string PropertyName { get; set; } = string.Empty;
    public string HandlerName { get; set; } = string.Empty;
    public RpcSecurityContext? SecurityContext { get; set; }
    public string RequestId { get; set; } = string.Empty;
}
```

### 5.2 Built-in Validators

**File**: `/src/Rpc/Orleans.Rpc.Security/Validation/Validators/BuiltInValidators.cs` (NEW)

```csharp
using System;
using System.Collections.Generic;

namespace Granville.Rpc.Security.Validation;

/// <summary>
/// Attribute to mark parameters as required (non-null).
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public class RequiredAttribute : ValidationAttribute
{
    public string? ErrorMessage { get; set; }

    public override ValidationResult Validate(object? value, ValidationContext context)
    {
        if (value == null)
        {
            return ValidationResult.Failure(
                ErrorMessage ?? "This field is required",
                "REQUIRED_MISSING");
        }

        if (value is string str && string.IsNullOrWhiteSpace(str))
        {
            return ValidationResult.Failure(
                ErrorMessage ?? "This field is required and cannot be empty",
                "REQUIRED_EMPTY");
        }

        return ValidationResult.Success();
    }
}

/// <summary>
/// Validates string length bounds.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public class StringLengthAttribute : ValidationAttribute
{
    public int MaxLength { get; }
    public int MinLength { get; set; } = 0;

    public StringLengthAttribute(int maxLength)
    {
        MaxLength = maxLength;
    }

    public override ValidationResult Validate(object? value, ValidationContext context)
    {
        if (value == null) return ValidationResult.Success();

        if (value is not string str)
        {
            return ValidationResult.Failure(
                "StringLength can only validate strings",
                "INVALID_TYPE");
        }

        if (str.Length < MinLength || str.Length > MaxLength)
        {
            return ValidationResult.Failure(
                $"String length must be between {MinLength} and {MaxLength}",
                "STRING_LENGTH_OUT_OF_RANGE",
                new Dictionary<string, object>
                {
                    { "min", MinLength },
                    { "max", MaxLength },
                    { "actual", str.Length }
                });
        }

        return ValidationResult.Success();
    }
}

/// <summary>
/// Validates numeric values within range.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public class RangeAttribute : ValidationAttribute
{
    public IComparable Minimum { get; }
    public IComparable Maximum { get; }

    public RangeAttribute(int minimum, int maximum)
        : this((IComparable)minimum, (IComparable)maximum) { }

    public RangeAttribute(double minimum, double maximum)
        : this((IComparable)minimum, (IComparable)maximum) { }

    public RangeAttribute(IComparable minimum, IComparable maximum)
    {
        Minimum = minimum;
        Maximum = maximum;
    }

    public override ValidationResult Validate(object? value, ValidationContext context)
    {
        if (value == null) return ValidationResult.Success();

        if (value is not IComparable comparable)
        {
            return ValidationResult.Failure(
                "Value must be comparable",
                "NOT_COMPARABLE");
        }

        if (comparable.CompareTo(Minimum) < 0 || comparable.CompareTo(Maximum) > 0)
        {
            return ValidationResult.Failure(
                $"Value must be between {Minimum} and {Maximum}",
                "VALUE_OUT_OF_RANGE",
                new Dictionary<string, object>
                {
                    { "min", Minimum },
                    { "max", Maximum },
                    { "actual", value }
                });
        }

        return ValidationResult.Success();
    }
}

/// <summary>
/// Base class for validation attributes.
/// </summary>
public abstract class ValidationAttribute : Attribute
{
    public abstract ValidationResult Validate(object? value, ValidationContext context);
}
```

### 5.3 Validation Filter

**File**: `/src/Rpc/Orleans.Rpc.Server/Filters/RpcValidationFilter.cs` (NEW)

See Section 4.1 for full implementation.

---

## 6. Game-Specific Validators

### 6.1 Movement Validation

```csharp
/// <summary>
/// Validates player movement for speed hacks and impossible teleportation.
/// </summary>
public class MovementValidator : IValidator<(float X, float Y, float Z)>
{
    private readonly IZoneManager _zoneManager;
    private readonly IGameStateCache _gameState;
    private readonly ILogger _logger;

    public ValidationResult Validate(
        (float X, float Y, float Z) position,
        ValidationContext context)
    {
        var (x, y, z) = position;

        // 1. Check bounds
        if (x < -10000 || x > 10000 || y < -10000 || y > 10000)
        {
            return ValidationResult.Failure(
                $"Coordinates out of world bounds: ({x}, {y})",
                "OUT_OF_BOUNDS");
        }

        // 2. Check terrain validity
        var zone = _zoneManager.GetZone(x, y);
        if (zone == null || !zone.IsAccessible)
        {
            return ValidationResult.Failure(
                $"Coordinates ({x}, {y}) are on inaccessible terrain",
                "INACCESSIBLE_TERRAIN");
        }

        // 3. Check for teleportation (speed hack)
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

                return ValidationResult.Failure(
                    $"Movement of {distance:F1}m exceeds expected {maxExpectedDistance:F1}m",
                    "TELEPORT_DETECTED");
            }
        }

        return ValidationResult.Success();
    }
}
```

### 6.2 Combat Validation

```csharp
/// <summary>
/// Validates combat actions for unrealistic fire rates and damage.
/// </summary>
public class CombatValidator : IValidator<CombatAction>
{
    private readonly IGameStateCache _gameState;

    public ValidationResult Validate(
        CombatAction? action,
        ValidationContext context)
    {
        if (action == null)
            return ValidationResult.Success();

        // 1. Check weapon exists
        var weapon = _gameState.GetWeapon(action.WeaponId);
        if (weapon == null)
        {
            return ValidationResult.Failure(
                $"Weapon {action.WeaponId} does not exist",
                "INVALID_WEAPON");
        }

        // 2. Check fire rate (no faster than weapon allows)
        var lastAttackTime = _gameState.GetLastAttackTime(context.SecurityContext?.PlayerId, action.WeaponId);

        if (lastAttackTime.HasValue)
        {
            var elapsed = (DateTime.UtcNow - lastAttackTime.Value).TotalSeconds;
            var minCooldown = weapon.FireRateSeconds;

            if (elapsed < minCooldown)
            {
                return ValidationResult.Failure(
                    $"Weapon is on cooldown for {minCooldown - elapsed:F2}s",
                    "WEAPON_COOLDOWN");
            }
        }

        // 3. Check damage is within weapon limits
        if (action.DamageAmount < weapon.MinDamage || action.DamageAmount > weapon.MaxDamage)
        {
            return ValidationResult.Failure(
                $"Damage {action.DamageAmount} outside weapon range [{weapon.MinDamage}, {weapon.MaxDamage}]",
                "INVALID_DAMAGE");
        }

        return ValidationResult.Success();
    }
}
```

---

## 7. Testing Strategy

### 7.1 Unit Tests

**File**: `/granville/test/Rpc.Security.Tests/ValidationTests.cs` (NEW)

```csharp
public class BuiltInValidatorTests
{
    [Fact]
    public void RequiredValidator_RejectsNull()
    {
        // Arrange
        var validator = new RequiredAttribute();
        var context = new ValidationContext { PropertyName = "PlayerId" };

        // Act
        var result = validator.Validate(null, context);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("REQUIRED_MISSING", result.ErrorCode);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(1000)]
    public void RangeValidator_RejectsOutOfRange(int value)
    {
        // Arrange
        var validator = new RangeAttribute(0, 100);
        var context = new ValidationContext { PropertyName = "Health" };

        // Act
        var result = validator.Validate(value, context);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("VALUE_OUT_OF_RANGE", result.ErrorCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    public void RangeValidator_AcceptsInRange(int value)
    {
        // Arrange
        var validator = new RangeAttribute(0, 100);

        // Act
        var result = validator.Validate(value, new ValidationContext());

        // Assert
        Assert.True(result.IsValid);
    }
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
