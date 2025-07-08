# Shim Requirements Clarification

## Initial Understanding (Incorrect)
We initially thought only 5 Orleans assemblies needed shims because only those were modified with InternalsVisibleTo attributes.

## Actual Requirements (Correct)
Through testing, we discovered that **any Orleans package that has transitive dependencies on the 5 modified assemblies also needs a shim**.

### The 5 Core Modified Assemblies
1. Orleans.Core.Abstractions
2. Orleans.Core
3. Orleans.Runtime  
4. Orleans.Serialization.Abstractions
5. Orleans.Serialization

### Additional Assemblies Requiring Shims
Due to transitive dependencies on the modified assemblies:

1. **Microsoft.Orleans.CodeGenerator** - Needs compile-time type resolution
2. **Microsoft.Orleans.Server** - Depends on Orleans.Runtime
3. **Microsoft.Orleans.Client** - Depends on Orleans.Core
4. **Microsoft.Orleans.Persistence.Memory** - Depends on Orleans.Runtime
5. **Microsoft.Orleans.Reminders** - Depends on Orleans.Core
6. **Microsoft.Orleans.Serialization.SystemTextJson** - Depends on Orleans.Serialization
7. **Microsoft.Orleans.Sdk** - Depends on Orleans.Core

## Why This Happens

When Microsoft.Orleans.Server references Orleans.Runtime:
- Without shim: It loads the official Orleans.Runtime.dll
- With our setup: We need Granville.Orleans.Runtime.dll
- Result: Type conflicts between Orleans.Runtime and Granville.Orleans.Runtime

The shim packages solve this by ensuring all references go to Granville assemblies.

## Conclusion

The "minimal shim" approach is not as minimal as initially thought. Any Orleans package that transitively depends on the 5 modified assemblies needs:
1. A Microsoft.Orleans.* shim package that forwards types
2. A corresponding Granville.Orleans.* implementation package

This explains why the original build scripts created shims for many more assemblies than just the 5 modified ones.