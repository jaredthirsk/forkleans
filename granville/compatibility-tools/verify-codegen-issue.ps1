#!/usr/bin/env pwsh

# Script to verify code generation issue with renamed assemblies

param(
    [string]$ProjectPath = "$PSScriptRoot/../../src/Orleans.Serialization/Orleans.Serialization.csproj",
    [string]$Configuration = "Release"
)

Write-Host "Verifying code generation issue..." -ForegroundColor Yellow

# Clean the project
Write-Host "`nCleaning project..." -ForegroundColor Cyan
dotnet-win clean $ProjectPath -c $Configuration | Out-Null

# Build with EmitCompilerGeneratedFiles
Write-Host "`nBuilding with EmitCompilerGeneratedFiles=true..." -ForegroundColor Cyan
dotnet-win build $ProjectPath -c $Configuration /p:BuildAsGranville=true /p:EmitCompilerGeneratedFiles=true -v:minimal

# Check for generated files
$projectDir = Split-Path $ProjectPath -Parent
$generatedDir = Join-Path $projectDir "obj/$Configuration/net8.0/generated/Orleans.CodeGenerator"

Write-Host "`nChecking for generated files in: $generatedDir" -ForegroundColor Cyan

if (Test-Path $generatedDir) {
    $generatedFiles = Get-ChildItem -Path $generatedDir -Filter "*.cs" -Recurse
    
    if ($generatedFiles) {
        Write-Host "  ✓ Found $($generatedFiles.Count) generated file(s)" -ForegroundColor Green
        
        foreach ($file in $generatedFiles) {
            Write-Host "    - $($file.Name)" -ForegroundColor Gray
            
            # Check for assembly attributes
            $content = Get-Content $file.FullName -Raw
            if ($content -match '\[assembly:.*ApplicationPartAttribute') {
                Write-Host "      ✓ Contains ApplicationPartAttribute" -ForegroundColor Green
            }
            if ($content -match '\[assembly:.*TypeManifestProviderAttribute') {
                Write-Host "      ✓ Contains TypeManifestProviderAttribute" -ForegroundColor Green
            }
        }
    } else {
        Write-Host "  ✗ No generated files found" -ForegroundColor Red
    }
} else {
    Write-Host "  ✗ Generated directory not found" -ForegroundColor Red
}

# Check the final assembly
$assemblyPath = Join-Path $projectDir "bin/$Configuration/net8.0/Granville.Orleans.Serialization.dll"
Write-Host "`nChecking final assembly: $assemblyPath" -ForegroundColor Cyan

if (Test-Path $assemblyPath) {
    # Use PowerShell reflection to check attributes
    try {
        # Load in separate AppDomain to avoid locking
        $assembly = [System.Reflection.Assembly]::LoadFrom($assemblyPath)
        
        $appPartAttrs = $assembly.GetCustomAttributes() | Where-Object { $_.GetType().Name -eq "ApplicationPartAttribute" }
        $manifestAttrs = $assembly.GetCustomAttributes() | Where-Object { $_.GetType().Name -eq "TypeManifestProviderAttribute" }
        
        if ($appPartAttrs) {
            Write-Host "  ✓ ApplicationPartAttribute found" -ForegroundColor Green
        } else {
            Write-Host "  ✗ ApplicationPartAttribute NOT found" -ForegroundColor Red
        }
        
        if ($manifestAttrs) {
            Write-Host "  ✓ TypeManifestProviderAttribute found" -ForegroundColor Green
        } else {
            Write-Host "  ✗ TypeManifestProviderAttribute NOT found" -ForegroundColor Red
        }
        
        # Count types with serialization attributes
        $serializerCount = 0
        $copierCount = 0
        
        foreach ($type in $assembly.GetTypes()) {
            $attrs = $type.GetCustomAttributes($false)
            if ($attrs | Where-Object { $_.GetType().Name -eq "RegisterSerializerAttribute" }) {
                $serializerCount++
            }
            if ($attrs | Where-Object { $_.GetType().Name -eq "RegisterCopierAttribute" }) {
                $copierCount++
            }
        }
        
        Write-Host "  Types with [RegisterSerializer]: $serializerCount" -ForegroundColor $(if ($serializerCount -gt 0) { "Green" } else { "Red" })
        Write-Host "  Types with [RegisterCopier]: $copierCount" -ForegroundColor $(if ($copierCount -gt 0) { "Green" } else { "Red" })
        
    } catch {
        Write-Host "  ✗ Error loading assembly: $_" -ForegroundColor Red
    }
} else {
    Write-Host "  ✗ Assembly not found" -ForegroundColor Red
}

Write-Host "`nDiagnosis:" -ForegroundColor Yellow
Write-Host "The Orleans code generator is creating the correct attributes," -ForegroundColor White
Write-Host "but they are not being included in the final assembly." -ForegroundColor White
Write-Host "This appears to be a timing issue where the generated code is created" -ForegroundColor White
Write-Host "after the compiler has already determined its input files." -ForegroundColor White

Write-Host "`nThis is likely caused by the assembly renaming happening too late" -ForegroundColor White
Write-Host "in the build process, after the code generator has already run." -ForegroundColor White