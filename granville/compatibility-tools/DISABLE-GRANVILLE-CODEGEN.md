# Disabling Granville Code Generation

When using both Microsoft.Orleans and Granville.Orleans packages in the same project, you may need to disable one of the code generators to avoid duplicate code generation errors.

## Automatic Disabling (Default Behavior)

**As of Granville Orleans 9.1.2.x, the official Orleans code generator is automatically disabled when you reference any of these Granville packages:**
- Granville.Orleans.Core
- Granville.Orleans.Sdk
- Granville.Orleans.CodeGenerator

This happens automatically - no configuration needed!

## Manual Control

If you need to manually control code generation behavior:

### To re-enable Official Orleans Code Generator
If for some reason you need the official Orleans generator to run alongside Granville's:

```xml
<PropertyGroup>
  <Orleans_DesignTimeBuild>false</Orleans_DesignTimeBuild>
</PropertyGroup>
```

### To manually disable Official Orleans Code Generator
This is now done automatically, but you can still set it explicitly:

```xml
<PropertyGroup>
  <Orleans_DesignTimeBuild>true</Orleans_DesignTimeBuild>
</PropertyGroup>
```

## Why This Works

The official Orleans source generator checks for the `orleans_designtimebuild` property and exits early when it's set to `true` (unless a debugger is attached). By setting this property, you effectively disable the Orleans code generator during normal builds.

## When to Use This

Use this approach when:
- You have both Microsoft.Orleans and Granville.Orleans packages in your project
- You're getting CS0101 duplicate type definition errors
- You want to use only Granville's code generation

## Note

The Granville fork's code generator checks for `granville_designtimebuild` instead of `orleans_designtimebuild`, so setting `Orleans_DesignTimeBuild` will only disable the official Orleans generator, not Granville's.