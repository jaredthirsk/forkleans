#!/usr/bin/env pwsh

# Test script for actual network transport benchmarks
# This script tests the raw transport framework with actual LiteNetLib networking

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$benchmarkDir = Split-Path -Parent $scriptDir

Write-Host "Starting actual network transport benchmark test..." -ForegroundColor Green

# Build the projects
Write-Host "Building benchmark projects..." -ForegroundColor Yellow
Set-Location $benchmarkDir
dotnet build src/Granville.Benchmarks.Runner/Granville.Benchmarks.Runner.csproj

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed"
    exit 1
}

# Create a test configuration for actual network testing
$configContent = @"
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "BenchmarkOptions": {
    "ClientCount": 5,
    "MessageSize": 256,
    "MessagesPerSecond": 30,
    "WarmupDuration": "00:00:02",
    "TestDuration": "00:00:05",
    "CooldownDuration": "00:00:01",
    "UseRawTransport": true,
    "UseActualTransport": true,
    "ServerHost": "127.0.0.1",
    "ServerPort": 12345,
    "Transports": [
      {
        "Type": "LiteNetLib",
        "Reliable": false,
        "Settings": {
          "disconnectTimeout": 5000,
          "maxConnectAttempts": 10
        }
      },
      {
        "Type": "LiteNetLib",
        "Reliable": true,
        "Settings": {
          "disconnectTimeout": 5000,
          "maxConnectAttempts": 10
        }
      }
    ],
    "NetworkConditions": [
      {
        "Name": "default",
        "LatencyMs": 0,
        "JitterMs": 0,
        "PacketLoss": 0.0,
        "Bandwidth": 0
      }
    ]
  }
}
"@

$configFile = Join-Path $benchmarkDir "config/test-actual-network.json"
Write-Host "Creating test configuration: $configFile" -ForegroundColor Yellow
$configContent | Out-File -FilePath $configFile -Encoding UTF8

# Run the benchmark
Write-Host "Running actual network transport benchmark..." -ForegroundColor Cyan
$runnerPath = Join-Path $benchmarkDir "src/Granville.Benchmarks.Runner/bin/Debug/net8.0/Granville.Benchmarks.Runner.dll"

try {
    dotnet $runnerPath $configFile
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ Actual network transport benchmark completed successfully!" -ForegroundColor Green
        
        # Show results if available
        $resultsDir = Join-Path $benchmarkDir "results/e2e"
        if (Test-Path $resultsDir) {
            $latestLog = Get-ChildItem $resultsDir -Filter "benchmark_log_*.txt" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
            if ($latestLog) {
                Write-Host "📊 Latest benchmark results:" -ForegroundColor Magenta
                Write-Host $latestLog.FullName -ForegroundColor Gray
                Write-Host ""
                Get-Content $latestLog.FullName -Tail 20
            }
        }
    }
    else {
        Write-Host "❌ Benchmark failed with exit code: $LASTEXITCODE" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}
catch {
    Write-Host "❌ Error running benchmark: $_" -ForegroundColor Red
    throw
}

Write-Host "Test completed!" -ForegroundColor Green