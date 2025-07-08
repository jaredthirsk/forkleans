#!/usr/bin/env pwsh
# Build and package essential Granville.Orleans packages

param(
    [string]$Configuration = "Release"
)

Write-Host "Building and packaging Granville.Orleans packages..." -ForegroundColor Green

$projects = @(
    "src/Orleans.Core.Abstractions/Orleans.Core.Abstractions.csproj",
    "src/Orleans.Core/Orleans.Core.csproj",
    "src/Orleans.Serialization.Abstractions/Orleans.Serialization.Abstractions.csproj",
    "src/Orleans.Serialization/Orleans.Serialization.csproj",
    "src/Orleans.CodeGenerator/Orleans.CodeGenerator.csproj",
    "src/Orleans.Analyzers/Orleans.Analyzers.csproj",
    "src/Orleans.Sdk/Orleans.Sdk.csproj",
    "src/Orleans.Runtime/Orleans.Runtime.csproj",
    "src/Orleans.Server/Orleans.Server.csproj",
    "src/Orleans.Persistence.Memory/Orleans.Persistence.Memory.csproj",
    "src/Orleans.Reminders/Orleans.Reminders.csproj",
    "src/Orleans.Serialization.SystemTextJson/Orleans.Serialization.SystemTextJson.csproj"
)

# First build all dependencies
Write-Host "Building Orleans.sln to ensure all dependencies are built..." -ForegroundColor Yellow
dotnet build Orleans.sln -c $Configuration -p:BuildAsGranville=true
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to build Orleans.sln"
    exit 1
}

# Then pack each project with proper naming
foreach ($project in $projects) {
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($project)
    Write-Host "`nPacking $projectName..." -ForegroundColor Cyan
    
    # Pack with Granville naming
    dotnet pack $project -c $Configuration -o Artifacts/Release --no-build `
        -p:BuildAsGranville=true `
        -p:Version="9.1.2.51"
        
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Failed to pack $project"
    }
}

Write-Host "`nGranville.Orleans packages created successfully!" -ForegroundColor Green
Write-Host "Packages are in: Artifacts/Release" -ForegroundColor Yellow