# OrleansCodeGen Type Forwarding Analysis

## The Question

Why can't we just forward the OrleansCodeGen types that already exist in the original Microsoft Orleans assemblies?

## The Problem

When the Shooter.Silo project compiles:
1. It references Granville.Orleans.Core.Abstractions (which contains `Granville.Orleans.Runtime.GrainId`)
2. The Orleans source generator sees `GrainId` and generates `OrleansCodeGen.Orleans.Runtime.Codec_GrainId`
3. The generated code expects this type to exist in Orleans.Core.Abstractions
4. But the generated codec is actually created in the Shooter.Silo assembly

## Why Not Use Microsoft's Generated Types?

The original Microsoft Orleans.Core.Abstractions.dll does contain generated types like `OrleansCodeGen.Orleans.Runtime.Codec_GrainId`. However, these codecs are compiled to work with the original type names:

```csharp
// In Microsoft's Orleans.Core.Abstractions.dll
namespace OrleansCodeGen.Orleans.Runtime
{
    public class Codec_GrainId : ICodec<Orleans.Runtime.GrainId>  // <-- References Orleans.Runtime.GrainId
    {
        // Implementation that serializes Orleans.Runtime.GrainId
    }
}
```

But in our Granville fork, the type is renamed:
```csharp
// In Granville.Orleans.Core.Abstractions.dll
namespace Orleans.Runtime
{
    public class GrainId  // <-- This is actually Granville.Orleans.Runtime.GrainId
    {
        // ...
    }
}
```

## The Mismatch - UPDATED UNDERSTANDING

Actually, the types should match correctly:
1. Our shim would forward `OrleansCodeGen.Orleans.Runtime.Codec_GrainId` to the Granville assembly
2. That codec expects to serialize `Orleans.Runtime.GrainId` (no Granville prefix in the namespace)
3. We are actually using `Orleans.Runtime.GrainId` (which is defined in Granville.Orleans.Core.Abstractions.dll)
4. The types match! The namespace is still `Orleans.Runtime`, only the assembly name changed

## The Real Issue

The issue is that:
1. When we build Granville.Orleans.Core.Abstractions.dll, the source generator should create `OrleansCodeGen.Orleans.Runtime.Codec_GrainId` inside it
2. But it seems the source generator isn't running when we build the Granville assemblies
3. So when Shooter.Silo references Granville assemblies, it generates the codecs itself
4. The runtime looks for these codecs in Orleans.Core.Abstractions (our shim)
5. Our shim forwards to Granville.Orleans.Core.Abstractions
6. But the codecs aren't there - they're in Shooter.Silo

## Why Phase 3 Might Be Needed

Different consuming projects might use different Orleans types that trigger code generation:
- Project A might use types X, Y, Z
- Project B might use types X, W, V
- Each project generates only the codecs it needs

So we need to:
1. Identify commonly used types (Phase 1 & 2)
2. Handle edge cases where projects use uncommon types (Phase 3)

## Alternative Consideration

Actually, there's another approach: Since the original Microsoft codecs won't work with our renamed types anyway, perhaps we should:
1. **Not** include OrleansCodeGen forwards in our shims at all
2. Let the consuming project's source generator create the codecs it needs
3. Document that projects using Orleans source generation must reference Granville.Orleans directly

This might be simpler and more correct than trying to forward types that won't work correctly anyway.