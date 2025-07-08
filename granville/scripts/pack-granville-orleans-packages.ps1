#!/usr/bin/env pwsh

# Pack Granville.Orleans.* packages
param(
    [string]$Configuration = "Release",
    [string]$Version = "9.1.2.51",
    [string]$OutputPath = "./Artifacts/Release"
)

Write-Host "Packing Granville.Orleans.* packages..." -ForegroundColor Green

# Create output directory
if (!(Test-Path $OutputPath)) {
    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
}

# Orleans projects to pack (as Granville.Orleans.*)
$projects = @(
    "src/Orleans.Core.Abstractions/Orleans.Core.Abstractions.csproj",
    "src/Orleans.Core/Orleans.Core.csproj",
    "src/Orleans.Serialization.Abstractions/Orleans.Serialization.Abstractions.csproj",
    "src/Orleans.Serialization/Orleans.Serialization.csproj",
    "src/Orleans.CodeGenerator/Orleans.CodeGenerator.csproj",
    "src/Orleans.Analyzers/Orleans.Analyzers.csproj",
    "src/Orleans.Runtime/Orleans.Runtime.csproj",
    "src/Orleans.Sdk/Orleans.Sdk.csproj",
    "src/Orleans.Server/Orleans.Server.csproj",
    "src/Orleans.Client/Orleans.Client.csproj",
    "src/Orleans.Persistence.Memory/Orleans.Persistence.Memory.csproj",
    "src/Orleans.Reminders/Orleans.Reminders.csproj",
    "src/Orleans.Serialization.SystemTextJson/Orleans.Serialization.SystemTextJson.csproj"
)

# Pack each project
foreach ($project in $projects) {
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($project)
    Write-Host "`nPacking $projectName..." -ForegroundColor Cyan
    
    & dotnet-win pack $project -c $Configuration -p:PackageVersion=$Version -p:BuildAsGranville=true -o $OutputPath --no-build -p:TreatWarningsAsErrors=false
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  ✓ Successfully created Granville.$projectName.$Version.nupkg" -ForegroundColor Green
    } else {
        Write-Host "  ✗ Failed to pack $projectName" -ForegroundColor Red
    }
}

Write-Host "`nPackaging complete!" -ForegroundColor Green
Write-Host "Packages created in: $OutputPath" -ForegroundColor Gray