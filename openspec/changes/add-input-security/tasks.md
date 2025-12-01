## 1. Type Policy Infrastructure (Orleans.Rpc.Security)

- [ ] 1.1 Create `TypeSafety/IRpcTypePolicy.cs` interface with `IsAllowed(Type)`, `OnRejected(Type)`
- [ ] 1.2 Create `TypeSafety/TypeWhitelist.cs` with allowed types, assemblies, denied types
- [ ] 1.3 Create `TypeSafety/TypePolicyOptions.cs` with Mode (Audit, Enforce), auto-discover settings
- [ ] 1.4 Create `TypeSafety/DefaultRpcTypePolicy.cs` implementing whitelist logic
- [ ] 1.5 Implement auto-discovery of `[GenerateSerializer]` types from loaded assemblies

## 2. Resource Limits (Orleans.Rpc.Security)

- [ ] 2.1 Create `TypeSafety/ResourceLimits.cs` with MaxDepth, MaxCollectionSize, MaxStringLength
- [ ] 2.2 Create depth tracking during deserialization
- [ ] 2.3 Create collection size validation during deserialization
- [ ] 2.4 Create string length validation during deserialization
- [ ] 2.5 Throw `RpcSecurityException` when limits exceeded

## 3. Orleans Serialization Integration

- [ ] 3.1 Research Orleans codec hooks for type validation
- [ ] 3.2 Create custom codec wrapper that checks type policy before deserializing
- [ ] 3.3 Register type policy check in Orleans serialization pipeline
- [ ] 3.4 Handle generic types (validate type arguments recursively)
- [ ] 3.5 Integration test: blocked type throws exception

## 4. Validation Attributes (Orleans.Rpc.Abstractions)

- [ ] 4.1 Create `Validation/ValidateAttribute.cs` (method-level, enables validation)
- [ ] 4.2 Create `Validation/RequiredAttribute.cs` (non-null check)
- [ ] 4.3 Create `Validation/StringLengthAttribute.cs` (min/max length)
- [ ] 4.4 Create `Validation/RangeAttribute.cs` (numeric bounds)
- [ ] 4.5 Create `Validation/ValidateObjectAttribute.cs` (recursive validation)
- [ ] 4.6 Create `Validation/ValidationResult.cs` (success/failure with errors)

## 5. Validator Infrastructure (Orleans.Rpc.Security)

- [ ] 5.1 Create `Validation/IValidator.cs` and `IValidator<T>` interfaces
- [ ] 5.2 Create `Validation/ValidationContext.cs` with parameter name, method, user
- [ ] 5.3 Create `Validation/ValidatorRegistry.cs` for custom validator registration
- [ ] 5.4 Create `Validation/AttributeValidatorFactory.cs` to create validators from attributes
- [ ] 5.5 Implement caching of validators per method (scan once at startup)

## 6. Built-in Validators (Orleans.Rpc.Security)

- [ ] 6.1 Implement `RequiredValidator` - reject null/empty
- [ ] 6.2 Implement `StringLengthValidator` - check length bounds
- [ ] 6.3 Implement `RangeValidator` - check numeric bounds
- [ ] 6.4 Implement `ObjectValidator` - recursively validate object properties
- [ ] 6.5 Create `CompositeValidator` for combining multiple validators

## 7. Validation Filter (Orleans.Rpc.Security)

- [ ] 7.1 Create `Filters/IRpcValidationFilter.cs` interface
- [ ] 7.2 Create `Filters/DefaultRpcValidationFilter.cs` with attribute scanning
- [ ] 7.3 Integrate validation filter into RpcConnection request pipeline
- [ ] 7.4 Return `RpcStatus.InvalidArgument` with validation error details
- [ ] 7.5 Log validation failures with parameter names and values (redacted)

## 8. Game-Specific Validators (Shooter Sample)

- [ ] 8.1 Create `CoordinateBoundsValidator` for X, Y, Z position limits
- [ ] 8.2 Create `VelocityValidator` for movement speed limits
- [ ] 8.3 Create `PlayerNameValidator` for length and character restrictions
- [ ] 8.4 Create `DamageValueValidator` for valid damage ranges

## 9. DI Registration

- [ ] 9.1 Create `AddRpcTypeSafety()` extension method
- [ ] 9.2 Create `AddRpcValidation()` extension method
- [ ] 9.3 Create `AddRpcInputSecurity()` that combines both
- [ ] 9.4 Support audit vs enforce mode via configuration

## 10. Shooter Sample Integration

- [ ] 10.1 Add `[Validate]` to game RPC methods
- [ ] 10.2 Add `[Range]` to coordinate parameters
- [ ] 10.3 Add `[StringLength]` to player name
- [ ] 10.4 Add `[Required]` to required parameters
- [ ] 10.5 Register game-specific validators
- [ ] 10.6 Wire up `AddRpcInputSecurity()` in ActionServer startup

## 11. Testing

- [ ] 11.1 Unit tests for each built-in validator
- [ ] 11.2 Unit tests for type whitelist (allow, deny, auto-discover)
- [ ] 11.3 Unit tests for resource limits (depth, collection size, string length)
- [ ] 11.4 Integration test: blocked type rejected
- [ ] 11.5 Integration test: invalid parameter returns InvalidArgument
- [ ] 11.6 Integration test: valid parameters pass through
- [ ] 11.7 Security test: gadget chain payload blocked

## 12. Documentation

- [ ] 12.1 Document type whitelist configuration
- [ ] 12.2 Document validation attribute usage
- [ ] 12.3 Document custom validator creation
- [ ] 12.4 Update SECURITY-RECAP.md to mark Phases 10-11 complete
