# OrleansCodeGen Resolution

## Understanding the Issue

After extensive investigation, I've discovered:

1. **OrleansCodeGen types in Microsoft DLLs**: The OrleansCodeGen types found in Microsoft's Orleans.Core.Abstractions.dll from DistributedTests are NOT built as part of Orleans itself. They are generated when DistributedTests is built with code generation enabled.

2. **Orleans design**: Orleans is designed so that OrleansCodeGen types are generated in the CONSUMING assembly, not in Orleans itself.

3. **The real problem**: Some Orleans internal code expects certain OrleansCodeGen types to exist in Orleans assemblies. This creates a chicken-and-egg problem.

## Why Granville Disabled Code Generation

Granville Orleans intentionally sets `Orleans_DesignTimeBuild=true` to prevent conflicts when both Microsoft.Orleans and Granville.Orleans packages are used together. This is a valid design choice.

## Solutions

### Option 1: Generate Types in Consuming Assemblies (Current)
- Let Shooter.Silo and other consuming projects generate the OrleansCodeGen types
- This is how Orleans is designed to work
- However, this fails when Orleans internals expect types in specific assemblies

### Option 2: Pre-generate Types for Granville
- Create a separate project that references all Granville Orleans assemblies
- Enable code generation in this project
- Extract the generated OrleansCodeGen types
- Use ILMerge or similar to inject them into Granville assemblies
- Complex but maintains the separation

### Option 3: Enable Code Generation Selectively
- Modify Granville build to enable code generation for specific assemblies
- Requires careful management to avoid conflicts
- Changes the Granville design philosophy

## Immediate Workaround

For now, the Shooter sample can work around this by:
1. Using the v64 shims without OrleansCodeGen forwards
2. Ensuring the Shooter projects have `Orleans_DesignTimeBuild=false`
3. The OrleansCodeGen types will be generated in the Shooter assemblies

## Long-term Solution

The Granville fork needs to decide whether to:
- Maintain the current design and accept limitations
- Pre-generate and inject OrleansCodeGen types
- Change the design to allow selective code generation

This is a fundamental architectural decision for the Granville Orleans fork.