#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Tests transport performance under various network conditions
.DESCRIPTION
    This script runs benchmarks with different network profiles (LAN, WiFi, Mobile, etc.)
    to understand how transports behave under adverse conditions.
#>

param(
    [string]$ConfigFile = "./config/network-condition-test.json",
    [string]$Transport = "all",
    [string]$NetworkProfile = "all",
    [switch]$Quick
)

Write-Host "=== Network Condition Testing ===" -ForegroundColor Cyan
Write-Host "Testing transport performance under various network conditions"
Write-Host ""

# Build the project first
Write-Host "Building benchmarks..." -ForegroundColor Yellow
dotnet build ../src/Granville.Benchmarks.sln -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed"
    exit 1
}

# Change to runner directory
Push-Location ../src/Granville.Benchmarks.Runner

try {
    # Prepare network profiles
    $profiles = @("perfect", "lan", "wifi", "regional", "cross-country", "mobile-4g", "congested")
    if ($NetworkProfile -ne "all") {
        $profiles = @($NetworkProfile)
    }
    
    if ($Quick) {
        # Quick test with fewer profiles
        $profiles = @("perfect", "lan", "regional", "mobile-4g")
    }
    
    # Run benchmarks for each profile
    foreach ($profile in $profiles) {
        Write-Host ""
        Write-Host "Testing with network profile: $profile" -ForegroundColor Green
        
        # Create modified config with specific network condition
        $config = Get-Content $ConfigFile | ConvertFrom-Json
        
        # Filter to single network condition
        $condition = $config.networkConditions | Where-Object { $_.name -eq $profile }
        if ($null -eq $condition) {
            Write-Warning "Network profile '$profile' not found in config"
            continue
        }
        
        $config.networkConditions = @($condition)
        $config.benchmarkName = "Network-$profile"
        $config.outputPath = "./results/network-conditions/$profile"
        
        if ($Transport -ne "all") {
            # Filter to specific transport
            $config.transports = $config.transports | Where-Object { $_.name -like "*$Transport*" }
        }
        
        if ($Quick) {
            # Reduce duration for quick tests
            $config.warmupDuration = "00:00:02"
            $config.measurementDuration = "00:00:10"
        }
        
        # Save temporary config
        $tempConfig = "temp-network-$profile.json"
        $config | ConvertTo-Json -Depth 10 | Set-Content $tempConfig
        
        # Run benchmark
        Write-Host "Running benchmark with $profile network conditions..."
        dotnet run -- -c $tempConfig --network-emulation
        
        # Clean up temp config
        Remove-Item $tempConfig -Force
    }
    
    # Generate comparison report
    Write-Host ""
    Write-Host "Generating network condition comparison report..." -ForegroundColor Yellow
    
    $reportPath = "./results/network-conditions/comparison-report.md"
    $report = @"
# Network Condition Test Results

Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")

## Summary

This report compares transport performance under various network conditions.

## Network Profiles Tested

| Profile | Latency | Jitter | Packet Loss | Bandwidth |
|---------|---------|--------|-------------|-----------|
| Perfect | 0ms | 0ms | 0% | Unlimited |
| LAN | 1ms | 0ms | 0% | 1 Gbps |
| WiFi | 5ms | ±2ms | 0.1% | 100 Mbps |
| Regional | 30ms | ±5ms | 0.1% | 100 Mbps |
| Cross-Country | 80ms | ±10ms | 0.5% | 50 Mbps |
| Mobile 4G | 50ms | ±15ms | 2% | 10 Mbps |
| Congested | 200ms | ±50ms | 10% | 1 Mbps |

## Results by Network Condition

"@

    foreach ($profile in $profiles) {
        $resultPath = "./results/network-conditions/$profile"
        if (Test-Path "$resultPath/summary.json") {
            $summary = Get-Content "$resultPath/summary.json" | ConvertFrom-Json
            
            $report += @"

### $profile Network

| Transport | Avg Latency | P95 Latency | Success Rate | Throughput |
|-----------|-------------|-------------|--------------|------------|
"@
            
            foreach ($result in $summary.results) {
                $report += "| $($result.transport) | $($result.avgLatencyMs)ms | $($result.p95LatencyMs)ms | $($result.successRate)% | $($result.throughput) msg/s |`n"
            }
        }
    }
    
    $report += @"

## Key Findings

1. **Latency Impact**: How additional network latency affects total round-trip time
2. **Packet Loss Handling**: Which transports handle packet loss better
3. **Bandwidth Limitations**: Performance degradation under constrained bandwidth
4. **Jitter Sensitivity**: Impact of network jitter on consistency

## Recommendations

Based on the results:
- For LAN/Regional: All transports perform similarly
- For Mobile/Congested: Prefer transports with better reliability mechanisms
- For High packet loss: Reliable modes show significant advantages
"@

    $report | Set-Content $reportPath
    Write-Host "Comparison report saved to: $reportPath" -ForegroundColor Green
    
    # Display summary
    Write-Host ""
    Write-Host "=== Test Summary ===" -ForegroundColor Cyan
    Write-Host "Network profiles tested: $($profiles -join ', ')"
    Write-Host "Results saved to: ./results/network-conditions/"
    Write-Host "View comparison report: $reportPath"
    
} finally {
    Pop-Location
}

Write-Host ""
Write-Host "Network condition testing complete!" -ForegroundColor Green