# PowerShell script to generate type forwarding shims for all Granville Orleans assemblies

param(
    [string]$Configuration = "Release",
    [string]$OutputPath = "shims"
)

$ErrorActionPreference = "Stop"

# Core assemblies that need shims
$assemblies = @(
    "Granville.Orleans.Core",
    "Granville.Orleans.Core.Abstractions", 
    "Granville.Orleans.Runtime",
    "Granville.Orleans.Serialization",
    "Granville.Orleans.Serialization.Abstractions",
    "Granville.Orleans.CodeGenerator",
    "Granville.Orleans.Analyzers",
    "Granville.Orleans.Reminders",
    "Granville.Orleans.Persistence.Memory",
    "Granville.Orleans.Server",
    "Granville.Orleans.Client",
    "Granville.Orleans.Sdk"
)

# Create output directory
New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null

Write-Host "Generating type forwarding shims..." -ForegroundColor Green

foreach ($assembly in $assemblies) {
    $searchPaths = @(
        "../../src/*/bin/$Configuration/net8.0/$assembly.dll",
        "../../src/*/bin/$Configuration/netstandard2.0/$assembly.dll",
        "../../src/*/bin/$Configuration/netstandard2.1/$assembly.dll"
    )
    
    $found = $false
    foreach ($searchPath in $searchPaths) {
        $files = Get-ChildItem -Path $searchPath -ErrorAction SilentlyContinue
        if ($files) {
            $assemblyPath = $files[0].FullName
            Write-Host "  Processing: $assembly" -ForegroundColor Yellow
            Write-Host "    Source: $assemblyPath"
            
            try {
                & dotnet-script GenerateTypeForwardingShims.csx $assemblyPath $OutputPath
                $found = $true
                break
            }
            catch {
                Write-Host "    Error: $_" -ForegroundColor Red
            }
        }
    }
    
    if (-not $found) {
        Write-Host "  Warning: Assembly not found: $assembly" -ForegroundColor Magenta
    }
}

Write-Host "`nShim generation complete!" -ForegroundColor Green
Write-Host "Shims created in: $OutputPath" -ForegroundColor Cyan