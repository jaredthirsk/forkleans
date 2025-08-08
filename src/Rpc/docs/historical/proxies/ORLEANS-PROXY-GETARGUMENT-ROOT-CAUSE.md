# Orleans Proxy GetArgument Null - Root Cause Found

## Summary

After extensive debugging, we've identified the root cause of why Orleans-generated proxies return null from `GetArgument()` despite having the argument values in their fields.

## The Root Cause

The issue is **NOT** in the generated code itself, but in how the invokable instances are managed:

1. **Generated code is correct**: The Orleans code generator creates proper GetArgument methods with switch statements that return field values
2. **Proxy populates fields correctly**: The generated proxy properly sets `request.arg0 = arg0` etc.
3. **BUT**: The invokable's Dispose() method clears all fields to null/default

## The Problem Flow

1. Proxy creates invokable: `var request = GetInvokable<TInvokable>()`
2. Proxy sets fields: `request.arg0 = "player-id"`
3. Proxy calls base: `base.InvokeAsync(request)`
4. **Somewhere in the call chain**, the invokable is disposed or pooled
5. Dispose() clears all fields: `arg0 = default` (null)
6. When RPC runtime calls `GetArgument(0)`, it returns null

## Evidence

From InvokableGenerator.cs:
```csharp
private MemberDeclarationSyntax GenerateDisposeMethod(...)
{
    // ...
    foreach (var field in fields)
    {
        if (field.IsInstanceField)
        {
            body.Add(
                ExpressionStatement(
                    AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression,
                        IdentifierName(field.FieldName),
                        LiteralExpression(SyntaxKind.DefaultLiteralExpression))));
        }
    }
    // ...
}
```

## Why Our Reflection Workaround Works

Our reflection-based approach reads the fields directly BEFORE they can be cleared:
1. We intercept at the right moment in the call chain
2. We read `arg0`, `arg1` etc. while they still have values
3. We use those values for RPC invocation

## The Real Fix

The proper fix would be one of:

1. **Don't dispose/pool invokables during active invocation** - Ensure the invokable lifetime extends through the entire RPC call
2. **Copy arguments early** - Extract arguments in the proxy before passing to base class
3. **Fix the dispose timing** - Only dispose after the invocation is complete

## Impact

This affects ALL Orleans-generated proxies when used with custom IGrainReferenceRuntime implementations that need to access invokable arguments after the initial proxy method call.