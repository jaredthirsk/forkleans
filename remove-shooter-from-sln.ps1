#!/usr/bin/env pwsh

# Script to remove Shooter projects from Orleans.sln

$slnFile = "Orleans.sln"
$content = Get-Content $slnFile -Raw

# Project GUIDs to remove
$shooterGuids = @(
    "{5E385F78-A7ED-49BD-017C-9827AE388FA3}",  # Shooter.Silo
    "{F1666446-CBEB-6470-CF7D-B9A8A554C1B7}",  # Shooter.ActionServer
    "{5A1524AC-4E40-4363-07C8-CC9937904428}",  # Shooter.AppHost
    "{C8FC349A-4C7F-DBA1-2688-4E94B7F34DBA}",  # Shooter.Client
    "{78347C6A-5548-1BCE-4B9B-FD8FEF6C4501}",  # Shooter.ServiceDefaults
    "{01872D28-36B0-D248-488F-E441C7BEFEDC}",  # Shooter.Shared
    "{B6FB8F67-CCAE-161C-B8D1-9AAF944B3D93}",  # Shooter.Bot
    "{B1986F02-4824-265F-DF61-F6FA11F920ED}"   # Shooter.Client.Common
)

# Remove Project definitions
foreach ($guid in $shooterGuids) {
    $pattern = "Project\([^)]+\)\s*=\s*[^,]+,\s*[^,]+,\s*`"$guid`"\s*\r?\nEndProject\r?\n"
    $content = $content -replace $pattern, ""
}

# Remove from GlobalSection(ProjectConfigurationPlatforms)
foreach ($guid in $shooterGuids) {
    $pattern = "\s*$guid\.Debug\|Any CPU\.ActiveCfg = Debug\|Any CPU\r?\n"
    $content = $content -replace $pattern, ""
    $pattern = "\s*$guid\.Debug\|Any CPU\.Build\.0 = Debug\|Any CPU\r?\n"
    $content = $content -replace $pattern, ""
    $pattern = "\s*$guid\.Release\|Any CPU\.ActiveCfg = Release\|Any CPU\r?\n"
    $content = $content -replace $pattern, ""
    $pattern = "\s*$guid\.Release\|Any CPU\.Build\.0 = Release\|Any CPU\r?\n"
    $content = $content -replace $pattern, ""
}

# Remove from GlobalSection(NestedProjects)
foreach ($guid in $shooterGuids) {
    $pattern = "\s*$guid = \{[A-F0-9-]+\}\r?\n"
    $content = $content -replace $pattern, ""
}

# Write the cleaned content back
Set-Content -Path $slnFile -Value $content -NoNewline

Write-Host "Removed Shooter projects from Orleans.sln" -ForegroundColor Green