#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Tests transport behavior under extreme stress conditions
.DESCRIPTION
    This script runs stress tests including connection storms, burst traffic,
    error injection, and resource exhaustion to validate Granville RPC
    resilience and recovery capabilities.
#>

param(
    [string]$ConfigFile = "./config/stress-test.json",
    [string]$StressType = "all",  # connection-storm, burst-traffic, error-injection, resource-exhaustion, mixed, all
    [string]$Transport = "all",
    [switch]$Quick,
    [switch]$Extreme  # Use more aggressive stress parameters
)

Write-Host "=== Stress Testing ===" -ForegroundColor Cyan
Write-Host "Testing transport resilience under extreme conditions"
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
    # Define stress test types
    $stressTypes = @(
        @{ Name = "connection-storm"; WorkloadName = "Connection-Storm"; Description = "Rapid connect/disconnect cycles" },
        @{ Name = "burst-traffic"; WorkloadName = "Burst-Traffic"; Description = "Sudden message volume spikes" },
        @{ Name = "error-injection"; WorkloadName = "Error-Injection"; Description = "Simulated network/protocol errors" },
        @{ Name = "resource-exhaustion"; WorkloadName = "Resource-Exhaustion"; Description = "Memory and connection pressure" },
        @{ Name = "mixed"; WorkloadName = "Mixed-Stress"; Description = "Combined stress conditions" }
    )
    
    # Filter stress types
    if ($StressType -ne "all") {
        $stressTypes = $stressTypes | Where-Object { $_.Name -eq $StressType }
        if ($stressTypes.Count -eq 0) {
            Write-Error "Unknown stress type: $StressType. Use: connection-storm, burst-traffic, error-injection, resource-exhaustion, mixed, or all"
            exit 1
        }
    }
    
    if ($Quick) {
        # Quick test - reduce to first 2 stress types
        $stressTypes = $stressTypes | Select-Object -First 2
    }
    
    # Run stress tests
    foreach ($stressTest in $stressTypes) {
        Write-Host ""
        Write-Host "Testing: $($stressTest.Description)" -ForegroundColor Green
        
        # Load base config
        $config = Get-Content $ConfigFile | ConvertFrom-Json
        
        # Filter to specific workload
        $config.workloads = $config.workloads | Where-Object { $_.name -eq $stressTest.WorkloadName }
        if ($config.workloads.Count -eq 0) {
            Write-Warning "Workload $($stressTest.WorkloadName) not found"
            continue
        }
        
        # Apply extreme parameters if requested
        if ($Extreme) {
            Write-Host "  Applying extreme stress parameters..." -ForegroundColor Yellow
            
            switch ($stressTest.Name) {
                "connection-storm" {
                    $config.workloads[0].clientCount = 200
                    $config.workloads[0].customSettings.StressInterval = "00:00:10"
                    $config.workloads[0].customSettings.MaxConcurrentConnections = 400
                }
                "burst-traffic" {
                    $config.workloads[0].clientCount = 100
                    $config.workloads[0].customSettings.BurstSize = 500
                    $config.workloads[0].customSettings.StressInterval = "00:00:15"
                }
                "error-injection" {
                    $config.workloads[0].clientCount = 150
                    $config.workloads[0].customSettings.ErrorInjectionRate = 0.25
                    $config.workloads[0].customSettings.StressInterval = "00:00:08"
                }
                "resource-exhaustion" {
                    $config.workloads[0].clientCount = 50
                    $config.workloads[0].customSettings.StressInterval = "00:00:20"
                }
                "mixed" {
                    $config.workloads[0].clientCount = 200
                    $config.workloads[0].customSettings.ErrorInjectionRate = 0.12
                    $config.workloads[0].customSettings.BurstSize = 200
                    $config.workloads[0].customSettings.StressInterval = "00:00:15"
                }
            }
        }
        
        # Filter transports if specified
        if ($Transport -ne "all") {
            $config.transports = $config.transports | Where-Object { $_.name -like "*$Transport*" }
            if ($config.transports.Count -eq 0) {
                Write-Warning "No transports match '$Transport'"
                continue
            }
        }
        
        # Quick test adjustments
        if ($Quick) {
            $config.warmupDuration = "00:00:03"
            $config.measurementDuration = "00:00:45"
            
            # Reduce to single transport for speed
            $config.transports = $config.transports | Select-Object -First 1
        }
        
        # Test each transport
        foreach ($transport in $config.transports) {
            Write-Host "  Testing with $($transport.name)..." -ForegroundColor Cyan
            
            # Create test-specific config
            $testConfig = $config | ConvertTo-Json -Depth 10 | ConvertFrom-Json
            $testConfig.transports = @($transport)
            $testConfig.outputPath = "./results/stress-tests/$($stressTest.Name)/$($transport.name)"
            $testConfig.benchmarkName = "Stress-$($stressTest.Name)-$($transport.name)"
            
            # Save temporary config
            $tempConfig = "temp-stress-$($stressTest.Name)-$($transport.name).json"
            $testConfig | ConvertTo-Json -Depth 10 | Set-Content $tempConfig
            
            # Run test
            $startTime = Get-Date
            dotnet run -- -c $tempConfig
            $endTime = Get-Date
            $duration = $endTime - $startTime
            
            # Analyze results
            $resultPath = $testConfig.outputPath
            if (Test-Path "$resultPath/summary.json") {
                $summary = Get-Content "$resultPath/summary.json" | ConvertFrom-Json
                $result = $summary.results[0]
                
                Write-Host "    Results: $($result.avgLatencyMs)ms avg, $($result.successRate)% success, $($result.throughput) msg/s" -ForegroundColor Yellow
                
                # Evaluate stress test success
                $stressSuccessMetrics = @()
                
                if ($result.successRate -gt 80) {
                    $stressSuccessMetrics += "Good resilience (>80% success)"
                } elseif ($result.successRate -gt 60) {
                    $stressSuccessMetrics += "Moderate resilience (60-80% success)"
                } else {
                    $stressSuccessMetrics += "Low resilience (<60% success)"
                }
                
                if ($result.avgLatencyMs -lt 200) {
                    $stressSuccessMetrics += "Low latency impact"
                } elseif ($result.avgLatencyMs -lt 500) {
                    $stressSuccessMetrics += "Moderate latency impact"
                } else {
                    $stressSuccessMetrics += "High latency impact"
                }
                
                Write-Host "    Assessment: $($stressSuccessMetrics -join ', ')" -ForegroundColor $(
                    if ($result.successRate -gt 80) { "Green" } 
                    elseif ($result.successRate -gt 60) { "Yellow" } 
                    else { "Red" }
                )
            }
            
            Write-Host "    Completed in $($duration.TotalMinutes.ToString("F1")) minutes" -ForegroundColor Green
            
            # Clean up temp config
            Remove-Item $tempConfig -Force
            
            # Brief pause between transports
            Start-Sleep -Seconds 2
        }
    }
    
    # Generate stress test analysis report
    Write-Host ""
    Write-Host "Generating stress test analysis..." -ForegroundColor Yellow
    
    $reportPath = "./results/stress-tests/stress-analysis.md"
    $report = @"
# Stress Test Analysis

Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Test Configuration: $(if ($Extreme) { "Extreme" } else { "Standard" })

## Test Types Executed

| Stress Type | Description | Purpose |
|-------------|-------------|---------|
| Connection Storm | Rapid connect/disconnect cycles | Test connection handling and cleanup |
| Burst Traffic | Sudden message volume spikes | Test throughput limits and queuing |
| Error Injection | Simulated network/protocol errors | Test error handling and recovery |
| Resource Exhaustion | Memory and connection pressure | Test resource management |
| Mixed Stress | Combined stress conditions | Test overall system resilience |

## Results Summary

| Test Type | Transport | Success Rate | Avg Latency | Assessment |
|-----------|-----------|--------------|-------------|------------|
"@

    # Collect results from all tests
    foreach ($stressTest in $stressTypes) {
        $testResults = @()
        
        # Check each transport result
        $config = Get-Content $ConfigFile | ConvertFrom-Json
        foreach ($transport in $config.transports) {
            $resultPath = "./results/stress-tests/$($stressTest.Name)/$($transport.name)"
            if (Test-Path "$resultPath/summary.json") {
                $summary = Get-Content "$resultPath/summary.json" | ConvertFrom-Json
                $result = $summary.results[0]
                
                $assessment = if ($result.successRate -gt 80) { "✅ Excellent" } 
                             elseif ($result.successRate -gt 60) { "⚠️ Acceptable" } 
                             else { "❌ Poor" }
                
                $report += "| $($stressTest.Name) | $($transport.name) | $($result.successRate)% | $($result.avgLatencyMs)ms | $assessment |`n"
            }
        }
    }
    
    $report += @"

## Key Findings

### Transport Resilience
- **Connection Storm Handling**: How well each transport manages rapid connection changes
- **Burst Traffic Capacity**: Maximum sustainable message throughput during spikes
- **Error Recovery**: Speed and effectiveness of recovery from network issues
- **Resource Efficiency**: Memory and connection usage under pressure

### Recommendations

Based on stress test results:
1. **Production Readiness**: Transports with >80% success rate under stress
2. **Monitoring Needs**: Focus on metrics that showed degradation
3. **Tuning Opportunities**: Parameters that could improve resilience
4. **Operational Limits**: Maximum safe load levels for each transport

### Next Steps
1. Review specific failure patterns in detailed logs
2. Implement monitoring for stress indicators
3. Create operational runbooks for recovery procedures
4. Consider circuit breaker patterns for high-stress scenarios
"@

    $report | Set-Content $reportPath
    Write-Host "Stress analysis saved to: $reportPath" -ForegroundColor Green
    
    # Display summary
    Write-Host ""
    Write-Host "=== Stress Test Summary ===" -ForegroundColor Cyan
    Write-Host "Tests completed: $($stressTypes.Name -join ', ')"
    Write-Host "Transports tested: $(if ($Transport -eq 'all') { 'All available' } else { $Transport })"
    Write-Host "Configuration: $(if ($Extreme) { 'Extreme stress' } else { 'Standard stress' })"
    Write-Host "Results saved to: ./results/stress-tests/"
    Write-Host "Analysis report: $reportPath"
    
    Write-Host ""
    Write-Host "Stress Test Insights:" -ForegroundColor Yellow
    Write-Host "- Connection storms test transport cleanup and connection pooling"
    Write-Host "- Burst traffic reveals throughput limits and queueing behavior"  
    Write-Host "- Error injection validates error handling and recovery mechanisms"
    Write-Host "- Resource exhaustion checks memory management and GC impact"
    Write-Host "- Mixed stress simulates realistic adverse conditions"
    
} finally {
    Pop-Location
}

Write-Host ""
Write-Host "Stress testing complete!" -ForegroundColor Green