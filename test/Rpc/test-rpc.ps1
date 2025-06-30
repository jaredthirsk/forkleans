#!/usr/bin/env pwsh

# Build all test projects
Write-Host "Building test projects..." -ForegroundColor Green
dotnet build Orleans.Rpc.TestGrains/Orleans.Rpc.TestGrains.csproj
dotnet build Orleans.Rpc.IntegrationTest.Server/Orleans.Rpc.IntegrationTest.Server.csproj
dotnet build Orleans.Rpc.IntegrationTest.Client/Orleans.Rpc.IntegrationTest.Client.csproj

# Start server in background
Write-Host "`nStarting server..." -ForegroundColor Green
$serverJob = Start-Job -ScriptBlock {
    Set-Location $using:PWD
    dotnet run --project Orleans.Rpc.IntegrationTest.Server/Orleans.Rpc.IntegrationTest.Server.csproj
}

# Wait for server to start
Start-Sleep -Seconds 3

# Run client
Write-Host "`nRunning client..." -ForegroundColor Green
dotnet run --project Orleans.Rpc.IntegrationTest.Client/Orleans.Rpc.IntegrationTest.Client.csproj

# Stop server
Write-Host "`nStopping server..." -ForegroundColor Green
Stop-Job $serverJob
Remove-Job $serverJob

Write-Host "`nTest complete!" -ForegroundColor Green