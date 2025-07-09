#!/usr/bin/env pwsh

$ErrorActionPreference = "Stop"

Write-Host "=== Extracting OrleansCodeGen Types from Microsoft Orleans ===" -ForegroundColor Cyan

# Create temp directory
$tempDir = "temp-orleans-extraction"
if (Test-Path $tempDir) {
    Remove-Item -Path $tempDir -Recurse -Force
}
New-Item -ItemType Directory -Path $tempDir | Out-Null

# Orleans assemblies to check
$assemblies = @(
    @{Name="Orleans.Core.Abstractions"; Version="9.0.0"},
    @{Name="Orleans.Core"; Version="9.0.0"},
    @{Name="Orleans.Serialization"; Version="9.0.0"},
    @{Name="Orleans.Serialization.Abstractions"; Version="9.0.0"}
)

$allTypes = @()

foreach ($assembly in $assemblies) {
    Write-Host "`nProcessing $($assembly.Name)..." -ForegroundColor Yellow
    
    $packageName = "Microsoft.$($assembly.Name)"
    $nupkgPath = "$tempDir/$packageName.nupkg"
    
    # Download package
    Write-Host "  Downloading $packageName v$($assembly.Version)..."
    $url = "https://www.nuget.org/api/v2/package/$packageName/$($assembly.Version)"
    Invoke-WebRequest -Uri $url -OutFile $nupkgPath -UseBasicParsing
    
    # Extract package
    $extractPath = "$tempDir/$packageName"
    Expand-Archive -Path $nupkgPath -DestinationPath $extractPath -Force
    
    # Find the DLL
    $dllPath = Get-ChildItem -Path $extractPath -Filter "$($assembly.Name).dll" -Recurse | Select-Object -First 1
    
    if ($dllPath) {
        Write-Host "  Found DLL: $($dllPath.FullName)"
        
        # Convert to Windows path for ildasm
        $windowsPath = $dllPath.FullName -replace '^/mnt/([a-z])/', '$1:/' -replace '/', '\'
        Write-Host "  Using Windows path: $windowsPath"
        
        # Use ildasm to extract type information
        $ildasmOutput = & dotnet-win ildasm $windowsPath
        
        # Find OrleansCodeGen types
        $orleansCodeGenTypes = $ildasmOutput | 
            Select-String -Pattern "class\s+.*?OrleansCodeGen\.[^\s]+" -AllMatches |
            ForEach-Object { $_.Matches } |
            ForEach-Object { 
                $match = $_.Value
                if ($match -match "(OrleansCodeGen\.[^\s]+)") {
                    $matches[1]
                }
            } |
            Select-Object -Unique
        
        # Also find types referenced in metadata
        $referencedTypes = $ildasmOutput | 
            Select-String -Pattern "ldtoken\s+class\s+OrleansCodeGen\.[^\s]+" -AllMatches |
            ForEach-Object { $_.Matches } |
            ForEach-Object { 
                $match = $_.Value
                if ($match -match "OrleansCodeGen\.[^\s]+") {
                    $matches[0]
                }
            } |
            Select-Object -Unique
        
        $allTypesInAssembly = @($orleansCodeGenTypes) + @($referencedTypes) | Select-Object -Unique
        
        Write-Host "  Found $($allTypesInAssembly.Count) OrleansCodeGen types"
        
        foreach ($type in $allTypesInAssembly) {
            $allTypes += [PSCustomObject]@{
                Assembly = $assembly.Name
                TypeName = $type
            }
        }
    }
}

# Remove duplicates and sort
$uniqueTypes = $allTypes | Sort-Object -Property TypeName -Unique

# Save results
Write-Host "`nSaving results..." -ForegroundColor Green
$uniqueTypes | Export-Csv -Path "orleans-codegen-types.csv" -NoTypeInformation

# Also create a simple text file with just the type names
$uniqueTypes.TypeName | Sort-Object | Set-Content -Path "orleans-codegen-types.txt"

# Group by pattern
Write-Host "`nType patterns found:" -ForegroundColor Cyan
$patterns = @{
    "Codec_" = @()
    "Copier_" = @()
    "Activator_" = @()
    "Proxy_" = @()
    "Invokable_" = @()
    "Metadata_" = @()
    "Other" = @()
}

foreach ($type in $uniqueTypes) {
    $typeName = $type.TypeName
    $matched = $false
    foreach ($pattern in $patterns.Keys | Where-Object { $_ -ne "Other" }) {
        if ($typeName -match $pattern) {
            $patterns[$pattern] += $typeName
            $matched = $true
            break
        }
    }
    if (-not $matched) {
        $patterns["Other"] += $typeName
    }
}

foreach ($pattern in $patterns.Keys | Sort-Object) {
    $count = $patterns[$pattern].Count
    if ($count -gt 0) {
        Write-Host "  $pattern : $count types"
    }
}

Write-Host "`nTotal unique types: $($uniqueTypes.Count)" -ForegroundColor Green
Write-Host "Results saved to:" -ForegroundColor Yellow
Write-Host "  - orleans-codegen-types.csv (detailed)"
Write-Host "  - orleans-codegen-types.txt (type names only)"

# Cleanup
Remove-Item -Path $tempDir -Recurse -Force