#!/usr/bin/env pwsh

# Remove Shooter projects from Orleans.sln using dotnet sln command

$shooterProjects = @(
    "samples/Rpc/Shooter.ActionServer/Shooter.ActionServer.csproj",
    "samples/Rpc/Shooter.AppHost/Shooter.AppHost.csproj",
    "samples/Rpc/Shooter.Bot/Shooter.Bot.csproj",
    "samples/Rpc/Shooter.Client.Common/Shooter.Client.Common.csproj",
    "samples/Rpc/Shooter.Client/Shooter.Client.csproj",
    "samples/Rpc/Shooter.ServiceDefaults/Shooter.ServiceDefaults.csproj",
    "samples/Rpc/Shooter.Shared/Shooter.Shared.csproj",
    "samples/Rpc/Shooter.Silo/Shooter.Silo.csproj"
)

foreach ($project in $shooterProjects) {
    Write-Host "Removing $project..." -ForegroundColor Gray
    dotnet sln Orleans.sln remove $project
}

Write-Host "`nShooter projects removed from Orleans.sln" -ForegroundColor Green