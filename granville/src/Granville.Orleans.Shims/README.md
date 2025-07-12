# Granville.Orleans.Shims

This package provides helper methods to ensure proper compatibility between Granville Orleans assemblies and Microsoft.Orleans shim packages.

## Purpose

Due to the way Orleans discovers serialization metadata at compile-time, the shim packages (e.g., `Microsoft.Orleans.Core`) cannot forward this metadata from the Granville assemblies. This package provides a workaround by explicitly registering Granville assemblies with the Orleans serializer.

## Usage

### Basic Usage

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add Orleans shim compatibility
builder.Services.AddOrleansShims();

builder.Host.UseOrleans(siloBuilder =>
{
    // Configure Orleans as normal
});
```

### With Custom Serializer Configuration

```csharp
builder.Services.AddOrleansShims(serializerBuilder =>
{
    // Add your own assemblies
    serializerBuilder.AddAssembly(typeof(MyGrain).Assembly);
    serializerBuilder.AddAssembly(typeof(IMyGrainInterface).Assembly);
});
```

### Manual Integration

If you're already calling `AddSerializer`, you can use the extension method:

```csharp
builder.Services.AddSerializer(serializerBuilder =>
{
    // Add Granville assemblies
    serializerBuilder.AddGranvilleAssemblies();
    
    // Add your own assemblies
    serializerBuilder.AddAssembly(typeof(MyGrain).Assembly);
});
```

## What This Package Does

1. **Early Loading**: Forces Granville assemblies to load early in the application lifecycle
2. **Assembly Registration**: Explicitly registers Granville assemblies with the Orleans serializer
3. **Metadata Discovery**: Ensures serialization metadata from Granville assemblies is available

## When You Need This

You need this package if:
- You're using Granville Orleans (renamed Orleans assemblies)
- You're using Microsoft.Orleans.* shim packages for compatibility
- You encounter `CodecNotFoundException` or similar serialization errors at startup

## Technical Background

For more details about the serialization shim issue, see:
`/granville/docs/SERIALIZATION-SHIM-ISSUE.md`