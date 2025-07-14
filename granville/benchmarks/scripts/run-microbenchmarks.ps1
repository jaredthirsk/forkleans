#!/usr/bin/env pwsh

param(
    [string]$Filter = "*",
    [string]$OutputPath = "../results/micro",
    [switch]$Quick
)

Write-Host "Running Granville RPC Micro-benchmarks..." -ForegroundColor Green

$projectPath = Join-Path $PSScriptRoot "../src/Granville.Benchmarks.Micro/Granville.Benchmarks.Micro.csproj"

# Ensure output directory exists
$outputDir = Join-Path $PSScriptRoot $OutputPath
if (!(Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# Build the project first
Write-Host "Building benchmark project..." -ForegroundColor Yellow
dotnet build $projectPath -c Release

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed!"
    exit 1
}

# Run benchmarks
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$artifactsPath = Join-Path $outputDir "BenchmarkDotNet.Artifacts_$timestamp"

$args = @(
    "--filter", $Filter,
    "--exporters", "json", "html", "csv",
    "--artifacts", $artifactsPath
)

if ($Quick) {
    $args += @("--job", "Dry")
}

Write-Host "Running benchmarks with filter: $Filter" -ForegroundColor Yellow
Write-Host "Results will be saved to: $artifactsPath" -ForegroundColor Yellow

dotnet run --project $projectPath -c Release -- $args

if ($LASTEXITCODE -eq 0) {
    Write-Host "Benchmarks completed successfully!" -ForegroundColor Green
    Write-Host "Results available at: $artifactsPath" -ForegroundColor Cyan
} else {
    Write-Error "Benchmarks failed!"
    exit 1
}