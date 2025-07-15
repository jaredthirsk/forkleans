#!/usr/bin/env pwsh

# Test script for raw transport benchmarks

Write-Host "Testing Granville RPC Raw Transport Benchmarks..." -ForegroundColor Green

# Build the benchmark project
Write-Host "Building benchmark runner..." -ForegroundColor Yellow
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$benchmarkDir = Split-Path -Parent $scriptDir
dotnet build "$benchmarkDir/src/Granville.Benchmarks.Runner" -c Release

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed! Fix the build errors before running benchmarks."
    exit 1
}

# Run a quick raw transport test
Write-Host "Running raw transport test..." -ForegroundColor Yellow
dotnet run --project "$benchmarkDir/src/Granville.Benchmarks.Runner" -c Release -- "$benchmarkDir/config/raw-transport-test.json"

if ($LASTEXITCODE -eq 0) {
    Write-Host "Raw transport test completed successfully!" -ForegroundColor Green
} else {
    Write-Error "Raw transport test failed!"
    exit 1
}

Write-Host "Test completed!" -ForegroundColor Green