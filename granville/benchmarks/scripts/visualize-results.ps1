#!/usr/bin/env pwsh

param(
    [Parameter(Mandatory=$true)]
    [string]$ResultsFile,
    [string]$OutputPath = "../results/charts",
    [switch]$OpenInBrowser
)

Write-Host "Generating visualizations for benchmark results..." -ForegroundColor Green

# Ensure output directory exists
$outputDir = Join-Path $PSScriptRoot $OutputPath
if (!(Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# Check if results file exists
if (!(Test-Path $ResultsFile)) {
    Write-Error "Results file not found: $ResultsFile"
    exit 1
}

# Generate HTML report with charts
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$htmlPath = Join-Path $outputDir "benchmark_report_$timestamp.html"

# Read JSON results
$results = Get-Content $ResultsFile | ConvertFrom-Json

# Group results for visualization
$workloadGroups = $results | Group-Object -Property WorkloadName

# Generate HTML with Chart.js
$html = @"
<!DOCTYPE html>
<html>
<head>
    <title>Granville RPC Benchmark Results</title>
    <script src="https://cdn.jsdelivr.net/npm/chart.js"></script>
    <style>
        body {
            font-family: Arial, sans-serif;
            margin: 20px;
            background-color: #f5f5f5;
        }
        .container {
            max-width: 1200px;
            margin: 0 auto;
            background-color: white;
            padding: 20px;
            border-radius: 8px;
            box-shadow: 0 2px 4px rgba(0,0,0,0.1);
        }
        h1, h2 {
            color: #333;
        }
        .chart-container {
            position: relative;
            height: 400px;
            margin: 30px 0;
        }
        .stats-grid {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(250px, 1fr));
            gap: 20px;
            margin: 20px 0;
        }
        .stat-card {
            background: #f8f9fa;
            padding: 15px;
            border-radius: 5px;
            border-left: 4px solid #007bff;
        }
        .stat-value {
            font-size: 24px;
            font-weight: bold;
            color: #007bff;
        }
        .stat-label {
            color: #666;
            font-size: 14px;
        }
    </style>
</head>
<body>
    <div class="container">
        <h1>Granville RPC Benchmark Results</h1>
        <p>Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")</p>
        
        <div class="stats-grid">
            <div class="stat-card">
                <div class="stat-value">$($results.Count)</div>
                <div class="stat-label">Total Benchmarks</div>
            </div>
            <div class="stat-card">
                <div class="stat-value">$($workloadGroups.Count)</div>
                <div class="stat-label">Workload Types</div>
            </div>
            <div class="stat-card">
                <div class="stat-value">$(($results | Select-Object -ExpandProperty TransportType -Unique).Count)</div>
                <div class="stat-label">Transport Types</div>
            </div>
        </div>
"@

# Add charts for each workload
$chartId = 0
foreach ($workloadGroup in $workloadGroups) {
    $workloadName = $workloadGroup.Name
    $workloadResults = $workloadGroup.Group
    
    $html += @"
        <h2>$workloadName</h2>
        
        <div class="chart-container">
            <canvas id="latencyChart$chartId"></canvas>
        </div>
        
        <div class="chart-container">
            <canvas id="throughputChart$chartId"></canvas>
        </div>
        
        <script>
        // Latency comparison chart
        new Chart(document.getElementById('latencyChart$chartId'), {
            type: 'bar',
            data: {
                labels: [$($workloadResults | ForEach-Object { "'$($_.TransportType) - $($_.NetworkCondition)'" } | Join-String -Separator ',')],
                datasets: [{
                    label: 'Average Latency (ms)',
                    data: [$($workloadResults | ForEach-Object { [math]::Round($_.Metrics.AverageLatency / 1000, 2) } | Join-String -Separator ',')],
                    backgroundColor: 'rgba(54, 162, 235, 0.5)',
                    borderColor: 'rgba(54, 162, 235, 1)',
                    borderWidth: 1
                },
                {
                    label: 'P99 Latency (ms)',
                    data: [$($workloadResults | ForEach-Object { [math]::Round($_.Metrics.P99Latency / 1000, 2) } | Join-String -Separator ',')],
                    backgroundColor: 'rgba(255, 99, 132, 0.5)',
                    borderColor: 'rgba(255, 99, 132, 1)',
                    borderWidth: 1
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    title: {
                        display: true,
                        text: 'Latency Comparison - $workloadName'
                    }
                },
                scales: {
                    y: {
                        beginAtZero: true,
                        title: {
                            display: true,
                            text: 'Latency (ms)'
                        }
                    }
                }
            }
        });
        
        // Throughput comparison chart
        new Chart(document.getElementById('throughputChart$chartId'), {
            type: 'line',
            data: {
                labels: [$($workloadResults | ForEach-Object { "'$($_.TransportType)'" } | Join-String -Separator ',')],
                datasets: [{
                    label: 'Messages per Second',
                    data: [$($workloadResults | ForEach-Object { [math]::Round($_.Metrics.MessagesPerSecond, 0) } | Join-String -Separator ',')],
                    borderColor: 'rgb(75, 192, 192)',
                    backgroundColor: 'rgba(75, 192, 192, 0.2)',
                    tension: 0.1
                },
                {
                    label: 'Error Rate (%)',
                    data: [$($workloadResults | ForEach-Object { [math]::Round($_.Metrics.ErrorRate * 100, 2) } | Join-String -Separator ',')],
                    borderColor: 'rgb(255, 99, 132)',
                    backgroundColor: 'rgba(255, 99, 132, 0.2)',
                    tension: 0.1,
                    yAxisID: 'y1'
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                interaction: {
                    mode: 'index',
                    intersect: false,
                },
                plugins: {
                    title: {
                        display: true,
                        text: 'Throughput & Reliability - $workloadName'
                    }
                },
                scales: {
                    y: {
                        type: 'linear',
                        display: true,
                        position: 'left',
                        title: {
                            display: true,
                            text: 'Messages/sec'
                        }
                    },
                    y1: {
                        type: 'linear',
                        display: true,
                        position: 'right',
                        title: {
                            display: true,
                            text: 'Error Rate (%)'
                        },
                        grid: {
                            drawOnChartArea: false,
                        }
                    }
                }
            }
        });
        </script>
"@
    
    $chartId++
}

$html += @"
    </div>
</body>
</html>
"@

# Write HTML file
$html | Out-File -FilePath $htmlPath -Encoding UTF8

Write-Host "Visualization generated: $htmlPath" -ForegroundColor Green

# Open in browser if requested
if ($OpenInBrowser) {
    Start-Process $htmlPath
}