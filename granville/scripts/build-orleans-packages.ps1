#!/usr/bin/env pwsh
# Build Orleans packages for assemblies modified with InternalsVisibleTo
# Based on /granville/fork-maintenance/MODIFICATIONS-TO-UPSTREAM.md
# Using hybrid package strategy - only build modified assemblies as Granville.Orleans.*

Write-Host "Building Granville Orleans packages (modified assemblies only)..." -ForegroundColor Green
Write-Host "Using hybrid package strategy - unmodified assemblies will use official Microsoft packages" -ForegroundColor Cyan

# Build assemblies that have InternalsVisibleTo modifications
# Plus Orleans.Sdk and Orleans.CodeGenerator which are needed by the shims
# Plus commonly used packages like Serialization.SystemTextJson for convenience
$projects = @(
    "src\Orleans.Core.Abstractions\Orleans.Core.Abstractions.csproj",
    "src\Orleans.Serialization.Abstractions\Orleans.Serialization.Abstractions.csproj",
    "src\Orleans.Core\Orleans.Core.csproj",
    "src\Orleans.Serialization\Orleans.Serialization.csproj",
    "src\Orleans.Runtime\Orleans.Runtime.csproj",
    "src\Orleans.Sdk\Orleans.Sdk.csproj",
    "src\Orleans.CodeGenerator\Orleans.CodeGenerator.csproj",
    "src\Orleans.Serialization.SystemTextJson\Orleans.Serialization.SystemTextJson.csproj"
)

Write-Host "`nModified assemblies to build:" -ForegroundColor Yellow
foreach ($project in $projects) {
    Write-Host "  - $project" -ForegroundColor Gray
}

Write-Host "`nUnmodified assemblies (use official Microsoft packages):" -ForegroundColor Yellow
Write-Host "  - Orleans.Client" -ForegroundColor Gray
Write-Host "  - Orleans.Server" -ForegroundColor Gray
Write-Host "  - Orleans.Reminders" -ForegroundColor Gray
Write-Host "  - Orleans.Serialization.SystemTextJson" -ForegroundColor Gray
Write-Host "  - Orleans.Persistence.Memory" -ForegroundColor Gray
Write-Host "  - All other Orleans extensions" -ForegroundColor Gray

Write-Host "`n"

foreach ($project in $projects) {
    Write-Host "Building and packing $project..." -ForegroundColor Cyan
    # Suppress NU5128 warning about missing lib/ref assemblies as these are source packages
    dotnet pack $project -c Release -o Artifacts/Release -p:NoWarn=NU5128
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Failed to build $project" -ForegroundColor Red
        exit 1
    }
}

Write-Host "`nAll Granville.Orleans packages built successfully!" -ForegroundColor Green
Write-Host "Packages are in: Artifacts/Release/" -ForegroundColor Cyan