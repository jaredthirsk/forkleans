#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Tests MMO-style workloads for massive multiplayer scenarios
.DESCRIPTION
    This script runs MMO scaling tests with increasing player counts,
    zone distribution, and cross-zone interactions to validate
    Granville RPC performance for large-scale multiplayer games.
#>

param(
    [string]$ConfigFile = "./config/mmo-scaling-test.json",
    [string]$Scale = "all",  # small, medium, large, massive, all
    [string]$Transport = "LiteNetLib-Unreliable",
    [switch]$Quick,
    [switch]$Progressive  # Start small and build up
)

Write-Host "=== MMO Scaling Test ===" -ForegroundColor Cyan
Write-Host "Testing massive multiplayer scenarios with zone distribution"
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
    # Define scale levels
    $scaleTests = @(
        @{ Name = "small"; ClientCount = 100; ZoneCount = 2; Description = "Small MMO (100 players, 2 zones)" },
        @{ Name = "medium"; ClientCount = 500; ZoneCount = 4; Description = "Medium MMO (500 players, 4 zones)" },
        @{ Name = "large"; ClientCount = 1000; ZoneCount = 8; Description = "Large MMO (1000 players, 8 zones)" },
        @{ Name = "massive"; ClientCount = 2000; ZoneCount = 10; Description = "Massive MMO (2000 players, 10 zones)" }
    )
    
    # Filter scales based on parameter
    if ($Scale -ne "all") {
        $scaleTests = $scaleTests | Where-Object { $_.Name -eq $Scale }
        if ($scaleTests.Count -eq 0) {
            Write-Error "Unknown scale: $Scale. Use: small, medium, large, massive, or all"
            exit 1
        }
    }
    
    if ($Quick) {
        # Quick test - reduce to first 2 scales and shorter duration
        $scaleTests = $scaleTests | Select-Object -First 2
    }
    
    # Test each scale
    foreach ($test in $scaleTests) {
        Write-Host ""
        Write-Host "Testing $($test.Description)..." -ForegroundColor Green
        
        if ($Progressive -and $test.ClientCount -gt 100) {
            Write-Host "Progressive mode: Starting with smaller load and ramping up..." -ForegroundColor Yellow
            
            # Start with 25% load for 30 seconds, then ramp to full
            $rampUpSteps = @(
                @{ Clients = [math]::Floor($test.ClientCount * 0.25); Duration = "00:00:30" },
                @{ Clients = [math]::Floor($test.ClientCount * 0.5); Duration = "00:00:30" },
                @{ Clients = [math]::Floor($test.ClientCount * 0.75); Duration = "00:00:30" },
                @{ Clients = $test.ClientCount; Duration = "00:01:00" }
            )
        } else {
            # Direct to full load
            $rampUpSteps = @(
                @{ Clients = $test.ClientCount; Duration = $Quick ? "00:01:00" : "00:02:00" }
            )
        }
        
        foreach ($step in $rampUpSteps) {
            Write-Host "  Running with $($step.Clients) clients for $($step.Duration)..." -ForegroundColor Cyan
            
            # Create modified config
            $configPath = if ([System.IO.Path]::IsPathRooted($ConfigFile)) {
                $ConfigFile
            } else {
                Join-Path $PSScriptRoot ".." $ConfigFile
            }
            $config = Get-Content $configPath | ConvertFrom-Json
            
            # Find the appropriate workload scale
            $workloadScale = switch ($test.Name) {
                "small" { "Small-Scale" }
                "medium" { "Medium-Scale" } 
                "large" { "Large-Scale" }
                "massive" { "Massive-Scale" }
                default { "Small-Scale" }
            }
            $workloadName = "MMO-$workloadScale"
            
            # Override client count and duration in BenchmarkOptions
            if ($config.BenchmarkOptions) {
                $config.BenchmarkOptions.ClientCount = $step.Clients
                $config.BenchmarkOptions.TestDuration = $step.Duration
                
                # Set appropriate message rate based on scale
                $config.BenchmarkOptions.MessagesPerSecond = switch ($test.Name) {
                    "small" { 30 }
                    "medium" { 20 }
                    "large" { 10 }
                    "massive" { 5 }
                    default { 30 }
                }
            }
            
            # Filter to specific transport (handle the nested BenchmarkOptions structure)
            if ($config.BenchmarkOptions -and $config.BenchmarkOptions.Transports) {
                # For now, just use the first transport that matches the type
                $transportType = if ($Transport -match "LiteNetLib") { "LiteNetLib" } else { "Ruffles" }
                $reliable = $Transport -match "Reliable"
                $filteredTransports = $config.BenchmarkOptions.Transports | 
                    Where-Object { $_.Type -eq $transportType -and $_.Reliable -eq $reliable }
                
                if ($filteredTransports) {
                    $config.BenchmarkOptions.Transports = @($filteredTransports | Select-Object -First 1)
                } else {
                    # Fallback to first available transport if no match
                    Write-Warning "No transport matching '$Transport' found, using first available"
                    $config.BenchmarkOptions.Transports = @($config.BenchmarkOptions.Transports | Select-Object -First 1)
                }
            }
            
            # Note: Output path is controlled by the runner based on its configuration
            # We can't set arbitrary properties on the config object
            
            if ($Quick -and $config.BenchmarkOptions) {
                $config.BenchmarkOptions.WarmupDuration = "00:00:05"
            }
            
            # Save temporary config
            $tempConfig = Join-Path $PSScriptRoot "temp-mmo-$($test.Name)-$($step.Clients).json"
            $config | ConvertTo-Json -Depth 10 | Set-Content $tempConfig
            
            # Run benchmark
            $startTime = Get-Date
            $runnerPath = Join-Path $PSScriptRoot "../src/Granville.Benchmarks.Runner/bin/Release/net8.0/Granville.Benchmarks.Runner.dll"
            dotnet $runnerPath $tempConfig
            $endTime = Get-Date
            $duration = $endTime - $startTime
            
            Write-Host "    Completed in $($duration.TotalMinutes.ToString("F1")) minutes" -ForegroundColor Green
            
            # Clean up temp config
            Remove-Item $tempConfig -Force
            
            # Brief pause between steps
            if ($rampUpSteps.Count -gt 1) {
                Start-Sleep -Seconds 5
            }
        }
        
        # Analyze results for this scale
        $resultPath = "./results/mmo-scaling/$($test.Name)-$($test.ClientCount)clients"
        if (Test-Path "$resultPath/summary.json") {
            $summary = Get-Content "$resultPath/summary.json" | ConvertFrom-Json
            $result = $summary.results[0]
            
            Write-Host "  Results: $($result.avgLatencyMs)ms avg, $($result.successRate)% success, $($result.throughput) msg/s" -ForegroundColor Yellow
            
            # Check for performance degradation
            if ($result.successRate -lt 95) {
                Write-Warning "  Low success rate ($($result.successRate)%) - may be hitting scaling limits"
            }
            if ($result.avgLatencyMs -gt 100) {
                Write-Warning "  High latency ($($result.avgLatencyMs)ms) - performance degradation detected"
            }
        }
    }
    
    # Generate scaling analysis report
    Write-Host ""
    Write-Host "Generating MMO scaling analysis..." -ForegroundColor Yellow
    
    $reportPath = "./results/mmo-scaling/scaling-analysis.md"
    $report = @"
# MMO Scaling Analysis

Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Transport: $Transport

## Test Configuration

| Scale | Players | Zones | Zones/Player | Cross-Zone Rate |
|-------|---------|-------|--------------|-----------------|
| Small | 100 | 2 | 50 | 5% |
| Medium | 500 | 4 | 125 | 8% |
| Large | 1000 | 8 | 125 | 10% |
| Massive | 2000 | 10 | 200 | 12% |

## Performance Results

| Scale | Avg Latency | P95 Latency | Success Rate | Throughput | Notes |
|-------|-------------|-------------|--------------|------------|-------|
"@

    foreach ($test in $scaleTests) {
        $resultPath = "./results/mmo-scaling/$($test.Name)-$($test.ClientCount)clients"
        if (Test-Path "$resultPath/summary.json") {
            $summary = Get-Content "$resultPath/summary.json" | ConvertFrom-Json
            $result = $summary.results[0]
            
            $notes = @()
            if ($result.successRate -lt 95) { $notes += "Low success rate" }
            if ($result.avgLatencyMs -gt 100) { $notes += "High latency" }
            if ($notes.Count -eq 0) { $notes += "Good performance" }
            
            $report += "| $($test.Name.ToUpper()) | $($result.avgLatencyMs)ms | $($result.p95LatencyMs)ms | $($result.successRate)% | $($result.throughput) msg/s | $($notes -join ', ') |`n"
        }
    }
    
    $report += @"

## Scaling Characteristics

### Connection Distribution
- **Zone-based distribution**: Players evenly distributed across zones
- **Cross-zone interactions**: $($Transport) handling inter-zone communication
- **Activity patterns**: Mixed idle/casual/active/intense player behaviors

### Performance Bottlenecks
- Monitor success rate drops below 95% (connection limits)
- Watch for latency increases above 100ms (processing limits) 
- Check throughput plateau (bandwidth/protocol limits)

### Recommendations
Based on results:
- **Sweet spot**: Best performance/cost ratio typically at [analyze results]
- **Scaling limit**: Performance degradation starts at [analyze results]
- **Zone strategy**: Optimal zones-per-player ratio is [analyze results]

## Transport-Specific Findings

### $Transport Performance
- **Connection handling**: [analyze connection stability]
- **Cross-zone efficiency**: [analyze cross-zone message performance]
- **Scale characteristics**: [analyze how performance changes with scale]
"@

    # Ensure directory exists
    $reportDir = Split-Path $reportPath
    if (!(Test-Path $reportDir)) {
        New-Item -ItemType Directory -Path $reportDir -Force | Out-Null
    }
    
    $report | Set-Content $reportPath
    Write-Host "Scaling analysis saved to: $reportPath" -ForegroundColor Green
    
    # Display summary
    Write-Host ""
    Write-Host "=== MMO Scaling Test Summary ===" -ForegroundColor Cyan
    Write-Host "Scales tested: $($scaleTests.Name -join ', ')"
    Write-Host "Transport: $Transport"
    Write-Host "Results saved to: ./results/mmo-scaling/"
    Write-Host "Analysis report: $reportPath"
    
    # Show final recommendations
    Write-Host ""
    Write-Host "Next Steps:" -ForegroundColor Yellow
    Write-Host "1. Review scaling analysis for performance characteristics"
    Write-Host "2. Test different transports: LiteNetLib vs Ruffles"
    Write-Host "3. Experiment with network conditions for realistic scenarios"
    Write-Host "4. Consider implementing connection pooling for massive scales"
    
} finally {
    Pop-Location
}

Write-Host ""
Write-Host "MMO scaling tests complete!" -ForegroundColor Green