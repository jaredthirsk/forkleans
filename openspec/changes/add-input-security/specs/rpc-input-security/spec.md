## ADDED Requirements

### Requirement: Type Whitelisting

The system SHALL only deserialize types that are explicitly whitelisted, blocking all other types by default.

#### Scenario: Whitelisted type allowed
- **WHEN** a serialized message contains a type marked with `[GenerateSerializer]`
- **AND** that type is from a trusted assembly
- **THEN** deserialization proceeds normally

#### Scenario: Unknown type blocked
- **WHEN** a serialized message contains a type not in the whitelist
- **THEN** deserialization fails with `RpcSecurityException`
- **AND** a security warning is logged with the type name

#### Scenario: Dangerous type blocked
- **WHEN** a serialized message contains `System.Diagnostics.Process` or similar dangerous type
- **THEN** deserialization is blocked regardless of other settings

### Requirement: Auto-Discovery of Safe Types

The system SHALL automatically discover and whitelist types marked with `[GenerateSerializer]` from application assemblies.

#### Scenario: Application types auto-whitelisted
- **WHEN** the application starts
- **THEN** all types with `[GenerateSerializer]` attribute are added to the whitelist
- **AND** no manual configuration is required for application types

### Requirement: Deserialization Depth Limit

The system SHALL enforce a maximum object graph depth (default 100) to prevent stack overflow attacks.

#### Scenario: Deep nesting rejected
- **WHEN** a serialized message contains object nesting deeper than 100 levels
- **THEN** deserialization fails with `RpcSecurityException`
- **AND** the message is logged as suspicious

### Requirement: Collection Size Limit

The system SHALL enforce a maximum collection size (default 10,000) to prevent memory exhaustion.

#### Scenario: Large collection rejected
- **WHEN** a serialized message contains a collection with more than 10,000 elements
- **THEN** deserialization fails with `RpcSecurityException`

### Requirement: String Length Limit

The system SHALL enforce a maximum string length (default 1MB) to prevent memory exhaustion.

#### Scenario: Large string rejected
- **WHEN** a serialized message contains a string longer than 1MB
- **THEN** deserialization fails with `RpcSecurityException`

### Requirement: Type Policy Audit Mode

The system SHALL support an audit mode that logs type policy violations without blocking them.

#### Scenario: Audit mode logs but allows
- **WHEN** type policy is in audit mode
- **AND** an unknown type is deserialized
- **THEN** a warning is logged
- **AND** deserialization proceeds

### Requirement: Validation Attribute

The system SHALL provide a `[Validate]` attribute to enable parameter validation on RPC methods.

#### Scenario: Validation enabled
- **WHEN** a method has `[Validate]` attribute
- **THEN** all parameter validators are executed before method invocation

#### Scenario: Validation disabled by default
- **WHEN** a method does not have `[Validate]` attribute
- **THEN** no validation is performed (zero overhead)

### Requirement: Required Validator

The system SHALL provide a `[Required]` attribute that rejects null or empty values.

#### Scenario: Null value rejected
- **WHEN** a parameter has `[Required]` attribute
- **AND** the value is null
- **THEN** the request returns `RpcStatus.InvalidArgument`
- **AND** the error message indicates which parameter was null

#### Scenario: Empty string rejected
- **WHEN** a string parameter has `[Required]` attribute
- **AND** the value is empty or whitespace
- **THEN** the request returns `RpcStatus.InvalidArgument`

### Requirement: StringLength Validator

The system SHALL provide a `[StringLength(min, max)]` attribute for string length validation.

#### Scenario: String too short
- **WHEN** a string is shorter than the minimum length
- **THEN** the request returns `RpcStatus.InvalidArgument`

#### Scenario: String too long
- **WHEN** a string is longer than the maximum length
- **THEN** the request returns `RpcStatus.InvalidArgument`

### Requirement: Range Validator

The system SHALL provide a `[Range(min, max)]` attribute for numeric value validation.

#### Scenario: Value below minimum
- **WHEN** a numeric parameter is below the minimum
- **THEN** the request returns `RpcStatus.InvalidArgument`

#### Scenario: Value above maximum
- **WHEN** a numeric parameter is above the maximum
- **THEN** the request returns `RpcStatus.InvalidArgument`

### Requirement: Object Validator

The system SHALL provide a `[ValidateObject]` attribute for recursive validation of complex objects.

#### Scenario: Nested object validated
- **WHEN** a parameter has `[ValidateObject]` attribute
- **THEN** validators on the object's properties are also executed

### Requirement: Custom Validators

The system SHALL support custom validators via `IValidator<T>` interface.

#### Scenario: Custom validator invoked
- **WHEN** a custom `IValidator<T>` is registered
- **AND** a parameter of type `T` is validated
- **THEN** the custom validator is invoked

### Requirement: Validation Error Details

The system SHALL return detailed validation errors to help clients fix invalid requests.

#### Scenario: Error details included
- **WHEN** validation fails
- **THEN** the response includes parameter name and validation message
- **AND** multiple errors are aggregated if multiple validations fail
