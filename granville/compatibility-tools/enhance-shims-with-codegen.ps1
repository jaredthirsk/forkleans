#!/usr/bin/env pwsh

$ErrorActionPreference = "Stop"

Write-Host "=== Enhancing Shims with OrleansCodeGen Type Forwards ===" -ForegroundColor Cyan

# Read the extracted types
if (-not (Test-Path "orleans-codegen-types.csv")) {
    Write-Host "ERROR: orleans-codegen-types.csv not found. Run extract-orleans-codegen-types.ps1 first." -ForegroundColor Red
    exit 1
}

$codegenTypes = Import-Csv -Path "orleans-codegen-types.csv"
Write-Host "Loaded $($codegenTypes.Count) OrleansCodeGen types" -ForegroundColor Green

# Group by assembly
$typesByAssembly = $codegenTypes | Group-Object -Property Assembly

foreach ($group in $typesByAssembly) {
    $assemblyName = $group.Name
    $types = $group.Group
    
    Write-Host "`nProcessing $assemblyName ($($types.Count) types)..." -ForegroundColor Yellow
    
    # Determine the shim file name
    $shimFile = "shims-proper/$assemblyName.cs"
    
    if (-not (Test-Path $shimFile)) {
        Write-Host "  Shim file not found: $shimFile" -ForegroundColor Red
        continue
    }
    
    # Read the existing shim content
    $shimContent = Get-Content -Path $shimFile -Raw
    
    # Find the last TypeForwardedTo line
    $lastForwardIndex = $shimContent.LastIndexOf('[assembly: TypeForwardedTo(')
    if ($lastForwardIndex -eq -1) {
        Write-Host "  No existing TypeForwardedTo found in shim" -ForegroundColor Red
        continue
    }
    
    # Find the end of that line
    $lineEndIndex = $shimContent.IndexOf(")]", $lastForwardIndex) + 2
    
    # Build the new type forwards
    $newForwards = @()
    $newForwards += ""
    $newForwards += "// OrleansCodeGen generated types"
    
    foreach ($type in $types | Sort-Object -Property TypeName) {
        $typeName = $type.TypeName
        
        # Skip entries that don't look like type names
        if ($typeName -notmatch '^OrleansCodeGen\.[A-Za-z0-9_.]+$') {
            continue
        }
        
        # Skip nested types and methods
        if ($typeName -match '::|<|>|\(') {
            continue
        }
        
        $newForwards += "[assembly: TypeForwardedTo(typeof($typeName))]"
    }
    
    if ($newForwards.Count -gt 2) {
        # Insert the new forwards after the last existing forward
        $beforeContent = $shimContent.Substring(0, $lineEndIndex)
        $afterContent = $shimContent.Substring($lineEndIndex)
        
        $newContent = $beforeContent + [Environment]::NewLine + ($newForwards -join [Environment]::NewLine) + $afterContent
        
        # Save the enhanced shim
        $backupFile = "$shimFile.backup"
        Copy-Item -Path $shimFile -Destination $backupFile -Force
        Set-Content -Path $shimFile -Value $newContent -NoNewline
        
        Write-Host "  Added $($newForwards.Count - 2) type forwards to $shimFile" -ForegroundColor Green
        Write-Host "  Backup saved to $backupFile" -ForegroundColor Gray
    }
    else {
        Write-Host "  No valid types to add" -ForegroundColor Yellow
    }
}

Write-Host "`nEnhancement complete!" -ForegroundColor Green
Write-Host "`nNext steps:" -ForegroundColor Cyan
Write-Host "1. Recompile the shims using compile-*.ps1 scripts"
Write-Host "2. Package the shims with increased version numbers"
Write-Host "3. Test with the Shooter sample"