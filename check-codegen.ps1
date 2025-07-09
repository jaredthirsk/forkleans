#!/usr/bin/env pwsh

param(
    [string]$DllPath = "src/Orleans.Core.Abstractions/bin/Release/net8.0/Granville.Orleans.Core.Abstractions.dll"
)

$fullPath = Join-Path $PWD $DllPath

if (Test-Path $fullPath) {
    $assembly = [System.Reflection.Assembly]::LoadFrom($fullPath)
    $orleansCodeGenTypes = $assembly.GetTypes() | Where-Object { $_.FullName -like "*OrleansCodeGen*" }
    
    Write-Host "Assembly: $fullPath"
    Write-Host "Total types: $($assembly.GetTypes().Count)"
    Write-Host "OrleansCodeGen types: $($orleansCodeGenTypes.Count)"
    
    if ($orleansCodeGenTypes.Count -gt 0) {
        Write-Host "`nFirst 10 OrleansCodeGen types:"
        $orleansCodeGenTypes | Select-Object -First 10 | ForEach-Object {
            Write-Host "  - $($_.FullName)"
        }
    }
} else {
    Write-Host "File not found: $fullPath"
}