#!/usr/bin/env pwsh

# Script to inspect assembly attributes using reflection

param(
    [string]$AssemblyPath = "$PSScriptRoot/../../Artifacts/bin/Orleans.Serialization/Release/net8.0/Granville.Orleans.Serialization.dll"
)

if (-not (Test-Path $AssemblyPath)) {
    Write-Error "Assembly not found: $AssemblyPath"
    exit 1
}

Write-Host "Inspecting assembly: $AssemblyPath" -ForegroundColor Yellow

# Load the assembly
try {
    $assembly = [System.Reflection.Assembly]::LoadFrom($AssemblyPath)
    Write-Host "Loaded assembly: $($assembly.FullName)" -ForegroundColor Green
} catch {
    Write-Error "Failed to load assembly: $_"
    exit 1
}

# Check for ApplicationPart attribute
Write-Host "`nChecking for ApplicationPart attribute:" -ForegroundColor Cyan
$appPartAttrs = $assembly.GetCustomAttributes() | Where-Object { $_.GetType().FullName -eq "Orleans.ApplicationPartAttribute" }
if ($appPartAttrs) {
    Write-Host "  ✓ Found ApplicationPart attribute" -ForegroundColor Green
    foreach ($attr in $appPartAttrs) {
        Write-Host "    - $attr" -ForegroundColor Gray
    }
} else {
    Write-Host "  ✗ No ApplicationPart attribute found" -ForegroundColor Red
}

# Check for TypeManifestProvider attribute
Write-Host "`nChecking for TypeManifestProvider attributes:" -ForegroundColor Cyan
$manifestAttrs = $assembly.GetCustomAttributes() | Where-Object { $_.GetType().FullName -eq "Orleans.Serialization.Configuration.TypeManifestProviderAttribute" }
if ($manifestAttrs) {
    Write-Host "  ✓ Found TypeManifestProvider attributes: $($manifestAttrs.Count)" -ForegroundColor Green
    foreach ($attr in $manifestAttrs) {
        Write-Host "    - Provider Type: $($attr.ProviderType.FullName)" -ForegroundColor Gray
    }
} else {
    Write-Host "  ✗ No TypeManifestProvider attributes found" -ForegroundColor Red
}

# List all custom attributes
Write-Host "`nAll assembly-level attributes:" -ForegroundColor Cyan
$allAttrs = $assembly.GetCustomAttributes()
foreach ($attr in $allAttrs) {
    Write-Host "  - $($attr.GetType().FullName)" -ForegroundColor Gray
}

# Check for types with RegisterSerializer/RegisterCopier attributes
Write-Host "`nChecking for types with serialization attributes:" -ForegroundColor Cyan
$serializerTypes = @()
$copierTypes = @()

foreach ($type in $assembly.GetTypes()) {
    $attrs = $type.GetCustomAttributes($false)
    
    $hasRegisterSerializer = $attrs | Where-Object { $_.GetType().Name -eq "RegisterSerializerAttribute" }
    $hasRegisterCopier = $attrs | Where-Object { $_.GetType().Name -eq "RegisterCopierAttribute" }
    
    if ($hasRegisterSerializer) {
        $serializerTypes += $type
    }
    if ($hasRegisterCopier) {
        $copierTypes += $type
    }
}

Write-Host "  Found $($serializerTypes.Count) types with [RegisterSerializer]" -ForegroundColor $(if ($serializerTypes.Count -gt 0) { "Green" } else { "Red" })
Write-Host "  Found $($copierTypes.Count) types with [RegisterCopier]" -ForegroundColor $(if ($copierTypes.Count -gt 0) { "Green" } else { "Red" })

# Show a few examples
if ($serializerTypes.Count -gt 0) {
    Write-Host "`n  Example serializers:" -ForegroundColor Gray
    $serializerTypes | Select-Object -First 5 | ForEach-Object {
        Write-Host "    - $($_.FullName)" -ForegroundColor Gray
    }
}

if ($copierTypes.Count -gt 0) {
    Write-Host "`n  Example copiers:" -ForegroundColor Gray
    $copierTypes | Select-Object -First 5 | ForEach-Object {
        Write-Host "    - $($_.FullName)" -ForegroundColor Gray
    }
}

# Check for ImmutableArray types specifically
Write-Host "`nChecking for ImmutableArray codecs:" -ForegroundColor Cyan
$immutableArrayCodec = $assembly.GetType("Orleans.Serialization.Codecs.ImmutableArrayCodec``1")
$immutableArrayCopier = $assembly.GetType("Orleans.Serialization.Codecs.ImmutableArrayCopier``1")

if ($immutableArrayCodec) {
    Write-Host "  ✓ Found ImmutableArrayCodec<T>" -ForegroundColor Green
    $attrs = $immutableArrayCodec.GetCustomAttributes($false)
    foreach ($attr in $attrs) {
        Write-Host "    - Attribute: $($attr.GetType().Name)" -ForegroundColor Gray
    }
} else {
    Write-Host "  ✗ ImmutableArrayCodec<T> not found" -ForegroundColor Red
}

if ($immutableArrayCopier) {
    Write-Host "  ✓ Found ImmutableArrayCopier<T>" -ForegroundColor Green
    $attrs = $immutableArrayCopier.GetCustomAttributes($false)
    foreach ($attr in $attrs) {
        Write-Host "    - Attribute: $($attr.GetType().Name)" -ForegroundColor Gray
    }
} else {
    Write-Host "  ✗ ImmutableArrayCopier<T> not found" -ForegroundColor Red
}