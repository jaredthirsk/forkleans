#!/usr/bin/env pwsh
# Build script for Shooter sample with Granville Orleans packages

# Parameters
param(
    [switch]$Clean,
    [switch]$NoBuild
)

Write-Host "Building Shooter sample with Granville Orleans packages..." -ForegroundColor Yellow

# Clean if requested
if ($Clean) {
    Write-Host "Cleaning all projects..." -ForegroundColor Cyan
    Get-ChildItem -Path . -Include bin,obj -Recurse -Directory | Remove-Item -Recurse -Force
}

if (-not $NoBuild) {
    # Build Shooter.Shared first with code generation disabled
    Write-Host "Building Shooter.Shared (code generation disabled)..." -ForegroundColor Cyan
    dotnet build Shooter.Shared/Shooter.Shared.csproj -c Release /p:OrleansGenerateCodeOnBuild=false /p:EnableBuildTimeCodeGen=false
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to build Shooter.Shared"
        exit 1
    }
    
    # Build remaining projects
    $projects = @(
        "Shooter.ServiceDefaults",
        "Shooter.Silo",
        "Shooter.ActionServer",
        "Shooter.Client",
        "Shooter.Bot",
        "Shooter.AppHost"
    )
    
    foreach ($project in $projects) {
        Write-Host "Building $project..." -ForegroundColor Cyan
        dotnet build "$project/$project.csproj" -c Release
        
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to build $project"
            exit 1
        }
    }
}

Write-Host "`nBuild complete!" -ForegroundColor Green
Write-Host "To run the application:" -ForegroundColor Yellow
Write-Host "  cd Shooter.AppHost" -ForegroundColor White
Write-Host "  dotnet run" -ForegroundColor White
Write-Host "`nAlternatively, to run individual components:" -ForegroundColor Yellow
Write-Host "  1. Start Silo: cd Shooter.Silo && dotnet run" -ForegroundColor White
Write-Host "  2. Start ActionServer(s): cd Shooter.ActionServer && dotnet run" -ForegroundColor White
Write-Host "  3. Start Client: cd Shooter.Client && dotnet run" -ForegroundColor White
Write-Host "  4. Start Bot(s): cd Shooter.Bot && dotnet run" -ForegroundColor White