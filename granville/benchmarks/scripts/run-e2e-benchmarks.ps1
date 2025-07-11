#!/usr/bin/env pwsh

param(
    [string]$ConfigFile = "../config/default.json",
    [string]$OutputPath = "../results/e2e",
    [switch]$SkipBuild,
    [switch]$GenerateReport
)

Write-Host "Running Granville RPC End-to-End Benchmarks..." -ForegroundColor Green

$projectPath = Join-Path $PSScriptRoot "../src/Granville.Benchmarks.Runner/Granville.Benchmarks.Runner.csproj"

# Ensure output directory exists
$outputDir = Join-Path $PSScriptRoot $OutputPath
if (!(Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# Build the project if not skipped
if (!$SkipBuild) {
    Write-Host "Building benchmark runner..." -ForegroundColor Yellow
    dotnet build $projectPath -c Release
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed!"
        exit 1
    }
}

# Load configuration
$configPath = Join-Path $PSScriptRoot $ConfigFile
if (!(Test-Path $configPath)) {
    Write-Error "Configuration file not found: $configPath"
    exit 1
}

Write-Host "Using configuration: $configPath" -ForegroundColor Yellow

# Run benchmarks
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$logFile = Join-Path $outputDir "benchmark_log_$timestamp.txt"

Write-Host "Starting benchmark run..." -ForegroundColor Yellow
Write-Host "Logs will be written to: $logFile" -ForegroundColor Yellow

# Set environment variable for custom config
$env:BENCHMARK_CONFIG = $configPath

# Run the benchmark runner
dotnet run --project $projectPath -c Release -- | Tee-Object -FilePath $logFile

if ($LASTEXITCODE -eq 0) {
    Write-Host "Benchmarks completed successfully!" -ForegroundColor Green
    
    # Find the latest results file
    $resultsFiles = Get-ChildItem -Path $outputDir -Filter "benchmark_results_*.json" | Sort-Object LastWriteTime -Descending
    
    if ($resultsFiles.Count -gt 0) {
        $latestResults = $resultsFiles[0].FullName
        Write-Host "Results saved to: $latestResults" -ForegroundColor Cyan
        
        # Generate visualization if requested
        if ($GenerateReport) {
            Write-Host "Generating visualization report..." -ForegroundColor Yellow
            & (Join-Path $PSScriptRoot "visualize-results.ps1") -ResultsFile $latestResults -OpenInBrowser
        }
    }
} else {
    Write-Error "Benchmarks failed! Check the log file: $logFile"
    exit 1
}