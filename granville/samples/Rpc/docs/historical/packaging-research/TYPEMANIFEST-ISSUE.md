# TypeManifestProviderBase Runtime Issue

## Problem
At runtime, we get:
```
System.TypeLoadException: Could not load type 'Orleans.Serialization.Configuration.TypeManifestProviderBase' 
from assembly 'Orleans.Serialization, Version=9.0.0.0, Culture=neutral, PublicKeyToken=null'.
```

## Root Cause
- Generated code creates classes that inherit from `TypeManifestProviderBase`
- The generated code has assembly attributes like `[assembly: Orleans.Serialization.Configuration.TypeManifestProvider]`
- At runtime, .NET tries to load `TypeManifestProviderBase` from Orleans.Serialization.dll
- Our shim only contains type-forwarding attributes, not the actual type
- Type forwarding doesn't work for base classes that generated code inherits from

## Why This Happens
Looking at the stack trace, `SerializerBuilderExtensions.AddAssembly` is scanning assemblies for attributes, which triggers loading of the attribute's base type.

## Possible Solutions

### 1. Add TypeManifestProviderBase to the Shim
Modify the shim generation to include actual type definitions for base classes, not just type forwarding.

### 2. Use the Cascading Shim Approach
Go back to using shim packages for all Orleans assemblies that have dependencies on the modified ones.

### 3. Disable Assembly Scanning
Configure Orleans to not scan certain assemblies for serialization attributes.

### 4. Create a Hybrid Shim
Include both:
- Actual type definitions for types that need to exist (like base classes)
- Type forwarding for everything else

### 5. Use Assembly Binding Redirects
At runtime, redirect all Orleans.* assembly loads to Granville.Orleans.* assemblies.

## Immediate Workaround
The quickest fix is to revert to the cascading shim approach where we use shims for all Orleans assemblies, not just the 5 modified ones.