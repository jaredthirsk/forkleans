# Analyze-PackageDependencies.ps1
# Script to analyze dependencies between Forkleans packages

param(
    [Parameter()]
    [string]$StartingPackage = "Orleans.Rpc.Client"
)

$ErrorActionPreference = "Stop"

Write-Host "Analyzing package dependencies starting from $StartingPackage..." -ForegroundColor Cyan

# Dictionary to store project dependencies
$projectDependencies = @{}

# Function to get project references from a csproj file
function Get-ProjectReferences {
    param($ProjectPath)
    
    if (-not (Test-Path $ProjectPath)) {
        return @()
    }
    
    [xml]$proj = Get-Content $ProjectPath
    $refs = @()
    
    # Get ProjectReference items
    $proj.Project.ItemGroup.ProjectReference | Where-Object { $_ } | ForEach-Object {
        $refPath = $_.Include
        # Convert relative path to project name
        $projectName = [System.IO.Path]::GetFileNameWithoutExtension($refPath)
        $refs += $projectName
    }
    
    return $refs
}

# Function to recursively analyze dependencies
function Analyze-Dependencies {
    param(
        [string]$ProjectName,
        [int]$Level = 0,
        [hashtable]$Visited = @{}
    )
    
    if ($Visited.ContainsKey($ProjectName)) {
        return
    }
    
    $Visited[$ProjectName] = $true
    
    # Find the project file
    $projectFile = Get-ChildItem -Path "src" -Recurse -Filter "$ProjectName.csproj" | Select-Object -First 1
    
    if (-not $projectFile) {
        Write-Host ("  " * $Level) + "❌ $ProjectName (not found)" -ForegroundColor Red
        return
    }
    
    $indent = "  " * $Level
    $packageName = $ProjectName -replace "^Orleans\.", "Forkleans."
    Write-Host "$indent📦 $packageName" -ForegroundColor Green
    
    # Get dependencies
    $deps = Get-ProjectReferences -ProjectPath $projectFile.FullName
    
    if ($deps.Count -gt 0) {
        foreach ($dep in $deps) {
            Analyze-Dependencies -ProjectName $dep -Level ($Level + 1) -Visited $Visited
        }
    }
}

# Start analysis
Write-Host "`nDependency tree:" -ForegroundColor Yellow
Analyze-Dependencies -ProjectName $StartingPackage

Write-Host "`n=== RPC Package Dependencies ===" -ForegroundColor Cyan

# Analyze each RPC package
$rpcProjects = @(
    "Orleans.Rpc.Abstractions",
    "Orleans.Rpc.Client",
    "Orleans.Rpc.Server",
    "Orleans.Rpc.Sdk",
    "Orleans.Rpc.Transport.LiteNetLib",
    "Orleans.Rpc.Transport.Ruffles"
)

foreach ($proj in $rpcProjects) {
    Write-Host "`n$proj dependencies:" -ForegroundColor Yellow
    Analyze-Dependencies -ProjectName $proj -Visited @{}
}

# Generate a minimal package list
Write-Host "`n=== Minimal Package Set ===" -ForegroundColor Cyan
Write-Host "Based on the analysis, here's the minimal set of packages needed:" -ForegroundColor Gray

$minimalPackages = @(
    "Forkleans.Core.Abstractions",
    "Forkleans.Core",
    "Forkleans.Serialization.Abstractions",
    "Forkleans.Serialization",
    "Forkleans.Rpc.Abstractions",
    "Forkleans.Rpc.Client",
    "Forkleans.Rpc.Server",
    "Forkleans.Rpc.Sdk",
    "Forkleans.CodeGenerator",
    "Forkleans.Analyzers",
    "Forkleans.Sdk"
)

Write-Host "`nFor RPC client applications:" -ForegroundColor Yellow
@(
    "Forkleans.Rpc.Client",
    "Forkleans.Rpc.Transport.LiteNetLib (or Ruffles)",
    "Forkleans.Rpc.Sdk (build-time only)"
) | ForEach-Object { Write-Host "  - $_" -ForegroundColor White }

Write-Host "`nFor RPC server applications:" -ForegroundColor Yellow
@(
    "Forkleans.Rpc.Server", 
    "Forkleans.Rpc.Transport.LiteNetLib (or Ruffles)",
    "Forkleans.Rpc.Sdk (build-time only)"
) | ForEach-Object { Write-Host "  - $_" -ForegroundColor White }