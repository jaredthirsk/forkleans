#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Simple test of network conditions using existing FPS workload
.DESCRIPTION
    This script tests the network condition framework using the existing
    FpsGameWorkload to verify the system is working before running more complex tests.
#>

param(
    [string]$NetworkProfile = "perfect"
)

Write-Host "=== Simple Network Test ===" -ForegroundColor Cyan
Write-Host "Testing network condition framework with FPS workload"
Write-Host ""

# Build the project first
Write-Host "Building benchmarks..." -ForegroundColor Yellow
dotnet build ./src/Granville.Benchmarks.Runner/Granville.Benchmarks.Runner.csproj -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed"
    exit 1
}

# Change to runner directory
Push-Location ./src/Granville.Benchmarks.Runner

try {
    Write-Host "Testing with network profile: $NetworkProfile" -ForegroundColor Green
    
    # Create a minimal configuration that works with existing BenchmarkOptions
    $config = @{
        "BenchmarkOptions" = @{
            "ClientCount" = 10
            "MessageSize" = 128
            "MessagesPerSecond" = 30
            "TestDuration" = "00:00:30"
            "WarmupDuration" = "00:00:05"
            "CooldownDuration" = "00:00:02"
            "UseRawTransport" = $false
            "UseActualTransport" = $false
            "ServerHost" = "127.0.0.1"
            "ServerPort" = 12345
            "TransportConfigs" = @(
                @{
                    "Type" = "LiteNetLib"
                    "Reliable" = $false
                    "Settings" = @{}
                }
            )
            "NetworkConditions" = @(
                @{
                    "Name" = $NetworkProfile
                    "LatencyMs" = if ($NetworkProfile -eq "perfect") { 0 } else { 30 }
                    "JitterMs" = if ($NetworkProfile -eq "perfect") { 0 } else { 5 }
                    "PacketLoss" = if ($NetworkProfile -eq "perfect") { 0.0 } else { 0.001 }
                    "Bandwidth" = 0
                }
            )
        }
    }
    
    # Save temporary config
    $tempConfig = "temp-simple-test.json"
    $config | ConvertTo-Json -Depth 10 | Set-Content $tempConfig
    
    Write-Host "Configuration created: $tempConfig"
    Write-Host "Running simple benchmark test..." -ForegroundColor Yellow
    
    # Run benchmark
    dotnet run -- --config $tempConfig
    
    Write-Host "Test completed!" -ForegroundColor Green
    
    # Clean up
    Remove-Item $tempConfig -Force
    
} finally {
    Pop-Location
}

Write-Host ""
Write-Host "Simple network test complete!" -ForegroundColor Green