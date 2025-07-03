#!/usr/bin/env pwsh
# Simple build of Granville.Rpc packages without depending on Granville.Orleans
# This allows using Granville.Rpc with official Microsoft.Orleans packages

param(
    [string]$Configuration = "Release"
)

Write-Host "Building Granville.Rpc packages (using Microsoft.Orleans)..." -ForegroundColor Green

# RPC projects to build
$rpcProjects = @(
    "src/Rpc/Orleans.Rpc.Abstractions",
    "src/Rpc/Orleans.Rpc.Client", 
    "src/Rpc/Orleans.Rpc.Server",
    "src/Rpc/Orleans.Rpc.Sdk",
    "src/Rpc/Orleans.Rpc.Transport.LiteNetLib",
    "src/Rpc/Orleans.Rpc.Transport.Ruffles"
)

# Clean RPC bin/obj directories
Write-Host "Cleaning previous RPC builds..." -ForegroundColor Yellow
foreach ($project in $rpcProjects) {
    $projectPath = Join-Path $project "bin"
    if (Test-Path $projectPath) {
        Remove-Item -Path $projectPath -Recurse -Force
    }
    $objPath = Join-Path $project "obj"
    if (Test-Path $objPath) {
        Remove-Item -Path $objPath -Recurse -Force
    }
}

# Create a temporary Directory.Build.props to override assembly naming
$tempProps = @"
<Project>
  <!-- Override to use standard Orleans naming instead of Granville.Orleans -->
  <PropertyGroup>
    <UseStandardOrleansNaming>true</UseStandardOrleansNaming>
  </PropertyGroup>
</Project>
"@

$tempPropsPath = "src/Rpc/Directory.Build.props.temp"
$tempProps | Out-File -FilePath $tempPropsPath -Encoding UTF8

# Rename existing Directory.Build.props if it exists
$existingProps = "src/Rpc/Directory.Build.props"
if (Test-Path $existingProps) {
    Move-Item $existingProps "$existingProps.backup" -Force
}
Move-Item $tempPropsPath $existingProps

try {
    # Build and pack each project
    foreach ($project in $rpcProjects) {
        Write-Host "`nBuilding $project..." -ForegroundColor Cyan
        
        dotnet build "$project/$([System.IO.Path]::GetFileName($project)).csproj" -c $Configuration
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Build failed for $project"
            exit 1
        }
        
        Write-Host "Packing $project..." -ForegroundColor Cyan
        dotnet pack "$project/$([System.IO.Path]::GetFileName($project)).csproj" -c $Configuration -o Artifacts/Release --no-build
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Pack failed for $project"
            exit 1
        }
    }
    
    Write-Host "`nGranville.Rpc packages built successfully!" -ForegroundColor Green
    Write-Host "Packages are in: Artifacts/Release" -ForegroundColor Yellow
}
finally {
    # Restore original Directory.Build.props
    if (Test-Path "$existingProps.backup") {
        Remove-Item $existingProps -Force -ErrorAction SilentlyContinue
        Move-Item "$existingProps.backup" $existingProps -Force
    }
}