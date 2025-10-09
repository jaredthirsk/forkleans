# Zone Transition Testing Strategies

This document provides comprehensive testing strategies for validating zone transition functionality and preventing regressions.

## 🧪 Testing Pyramid for Zone Transitions

### Unit Tests (Foundation)
**Focus**: Individual components in isolation
**Coverage**: 70% of total test effort
**Tools**: xUnit, Moq, NSubstitute

### Integration Tests (Critical)  
**Focus**: Component interactions and RPC communication
**Coverage**: 25% of total test effort
**Tools**: TestContainers, custom test harness

### End-to-End Tests (Validation)
**Focus**: Full system behavior with real network
**Coverage**: 5% of total test effort  
**Tools**: Automated bots, log analysis scripts

---

## 🎯 Unit Testing Strategies

### Core Components to Test

#### 1. Zone Transition Debouncer
```csharp
public class ZoneTransitionDebouncerTests
{
    private readonly ZoneTransitionDebouncer _debouncer;
    private readonly Mock<ILogger<ZoneTransitionDebouncer>> _mockLogger;

    [Fact]
    public async Task ShouldTransitionAsync_WhenPlayerWellInsideZone_ShouldAllow()
    {
        // Arrange
        var newZone = new GridSquare(1, 0);
        var playerPosition = new Vector2(550, 250); // Well inside zone (1,0)
        var transitionCalled = false;
        
        // Act
        var result = await _debouncer.ShouldTransitionAsync(
            newZone, 
            playerPosition, 
            () => { transitionCalled = true; return Task.CompletedTask; });
        
        // Assert
        Assert.True(result);
        Assert.True(transitionCalled);
    }

    [Fact]
    public async Task ShouldTransitionAsync_WhenPlayerNearBoundary_ShouldBlock()
    {
        // Arrange - Player just 1 unit into zone (less than 2f hysteresis)
        var newZone = new GridSquare(1, 0);
        var playerPosition = new Vector2(501, 250); // Only 1 unit into zone
        
        // Act
        var result = await _debouncer.ShouldTransitionAsync(
            newZone, 
            playerPosition, 
            () => Task.CompletedTask);
        
        // Assert
        Assert.False(result);
        _mockLogger.Verify(x => x.Log(LogLevel.Warning, 
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("BLOCKED")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception, string>>()), 
            Times.Once);
    }

    [Fact]
    public async Task ShouldTransitionAsync_WhenRapidTransitions_ShouldEnterCooldown()
    {
        // Arrange - Simulate MAX_RAPID_TRANSITIONS attempts
        var zone1 = new GridSquare(1, 0);
        var zone2 = new GridSquare(0, 0);
        
        // Act - Attempt rapid transitions
        for (int i = 0; i < 9; i++) // Exceed MAX_RAPID_TRANSITIONS (8)
        {
            await _debouncer.ShouldTransitionAsync(
                i % 2 == 0 ? zone1 : zone2,
                new Vector2(550, 250),
                () => Task.CompletedTask);
        }
        
        // Final attempt should be blocked due to cooldown
        var result = await _debouncer.ShouldTransitionAsync(zone1, new Vector2(550, 250), () => Task.CompletedTask);
        
        // Assert
        Assert.False(result);
        Assert.True(_debouncer.IsInCooldown);
    }
}
```

#### 2. Health Monitor Testing
```csharp
public class ZoneTransitionHealthMonitorTests
{
    [Fact]
    public void UpdatePlayerPosition_WhenZoneMismatch_ShouldIncrementCounterOncePerSecond()
    {
        // Arrange
        var monitor = new ZoneTransitionHealthMonitor(_mockLogger.Object);
        monitor.UpdateServerZone(new GridSquare(0, 0)); // Server thinks player in (0,0)
        
        // Act - Multiple rapid position updates in different zone
        var position1 = new Vector2(550, 250); // Zone (1,0)
        monitor.UpdatePlayerPosition(position1);
        
        // Immediate second update (same second)
        monitor.UpdatePlayerPosition(new Vector2(551, 251));
        
        // Verify only one mismatch counted
        var diagnostics = monitor.GetDiagnostics();
        Assert.Contains("RapidCount: 1", diagnostics);
        
        // Wait 1+ second and update again
        Thread.Sleep(1100);
        monitor.UpdatePlayerPosition(new Vector2(552, 252));
        
        // Now should have 2 mismatches
        diagnostics = monitor.GetDiagnostics();
        Assert.Contains("RapidCount: 2", diagnostics);
    }

    [Fact]  
    public void UpdateServerZone_WhenServerZoneChanges_ShouldResetMismatchCounter()
    {
        // Arrange
        var monitor = new ZoneTransitionHealthMonitor(_mockLogger.Object);
        monitor.UpdateServerZone(new GridSquare(0, 0));
        monitor.UpdatePlayerPosition(new Vector2(550, 250)); // Different zone
        
        Thread.Sleep(1100);
        monitor.UpdatePlayerPosition(new Vector2(551, 251)); // Increment counter
        
        // Act - Update server zone to match player zone
        monitor.UpdateServerZone(new GridSquare(1, 0));
        
        // Assert - Counter should be reset
        var diagnostics = monitor.GetDiagnostics();
        Assert.Contains("RapidCount: 0", diagnostics);
    }
}
```

#### 3. Fire-and-Forget Pattern Testing
```csharp
public class SendPlayerInputTests  
{
    [Fact]
    public void SendPlayerInputEx_ShouldNotBlock_EvenWhenRpcSlow()
    {
        // Arrange
        var slowTask = new TaskCompletionSource<object>();
        _mockGrain.Setup(x => x.UpdatePlayerInputEx(It.IsAny<string>(), It.IsAny<Vector2?>(), It.IsAny<Vector2?>()))
                  .Returns(slowTask.Task); // Never completes
        
        var stopwatch = Stopwatch.StartNew();
        
        // Act
        _client.SendPlayerInputEx(Vector2.Zero, null);
        
        // Assert - Should return immediately  
        stopwatch.Stop();
        Assert.True(stopwatch.ElapsedMilliseconds < 100); // Should be nearly instant
        
        // Verify RPC was still called
        _mockGrain.Verify(x => x.UpdatePlayerInputEx(It.IsAny<string>(), It.IsAny<Vector2?>(), It.IsAny<Vector2?>()),
                         Times.Once);
    }

    [Fact]
    public async Task SendPlayerInputEx_WhenRpcTimesOut_ShouldMarkDisconnected()
    {
        // Arrange
        _mockGrain.Setup(x => x.UpdatePlayerInputEx(It.IsAny<string>(), It.IsAny<Vector2?>(), It.IsAny<Vector2?>()))
                  .ThrowsAsync(new TimeoutException());
        
        var serverChangedCalled = false;
        _client.ServerChanged += (_) => { serverChangedCalled = true; };
        
        // Act
        _client.SendPlayerInputEx(Vector2.Zero, null);
        
        // Wait for background task to complete
        await Task.Delay(200);
        
        // Assert
        Assert.False(_client.IsConnected);
        Assert.True(serverChangedCalled);
    }
}
```

---

## 🔗 Integration Testing Strategies

### Test Environment Setup

#### Lightweight Test Harness
```csharp
public class ZoneTransitionIntegrationTestFixture : IDisposable
{
    public TestServer SiloServer { get; private set; }
    public TestServer ActionServer1 { get; private set; }  
    public TestServer ActionServer2 { get; private set; }
    public GranvilleRpcGameClientService TestClient { get; private set; }

    public async Task InitializeAsync()
    {
        // Start test Orleans silo
        SiloServer = CreateTestSilo();
        await SiloServer.StartAsync();
        
        // Start test ActionServers for zones (0,0) and (1,0)
        ActionServer1 = CreateTestActionServer(new GridSquare(0, 0));
        ActionServer2 = CreateTestActionServer(new GridSquare(1, 0));
        
        await Task.WhenAll(
            ActionServer1.StartAsync(),
            ActionServer2.StartAsync()
        );
        
        // Create test client
        TestClient = new GranvilleRpcGameClientService(
            CreateTestLogger<GranvilleRpcGameClientService>(),
            CreateTestConfiguration());
    }
}
```

#### Integration Test Cases
```csharp
public class ZoneTransitionIntegrationTests : IClassFixture<ZoneTransitionIntegrationTestFixture>
{
    [Fact]
    public async Task FullZoneTransition_WhenPlayerMovesZones_ShouldTransitionSuccessfully()
    {
        // Arrange
        var testPlayer = "TestPlayer1";
        await _fixture.TestClient.ConnectAsync(testPlayer);
        
        // Verify initial connection
        Assert.True(_fixture.TestClient.IsConnected);
        
        // Act - Simulate player movement to different zone
        await _fixture.TestClient.PerformZoneTransition(new GridSquare(1, 0));
        
        // Assert
        await AssertEventually(
            () => _fixture.TestClient.CurrentZone?.Equals(new GridSquare(1, 0)) == true,
            timeout: TimeSpan.FromSeconds(5),
            message: "Zone transition should complete within 5 seconds"
        );
    }

    [Fact]
    public async Task ZoneTransition_WhenTargetServerUnavailable_ShouldRetryAndFallback()
    {
        // Arrange
        await _fixture.TestClient.ConnectAsync("TestPlayer1");
        
        // Shutdown target ActionServer to simulate failure
        await _fixture.ActionServer2.StopAsync();
        
        // Act - Attempt transition to unavailable zone
        await _fixture.TestClient.PerformZoneTransition(new GridSquare(1, 0));
        
        // Assert - Should handle failure gracefully
        await Task.Delay(6000); // Wait for forced transition timeout
        
        // Should still be connected to original server or show appropriate error state
        Assert.True(_fixture.TestClient.IsConnected || 
                   _fixture.TestClient.LastError?.Contains("connection") == true);
    }

    [Fact] 
    public async Task ConcurrentZoneTransitions_WhenMultipleClients_ShouldNotInterfere()
    {
        // Arrange
        var client1 = CreateTestClient("Player1");
        var client2 = CreateTestClient("Player2"); 
        var client3 = CreateTestClient("Player3");
        
        await Task.WhenAll(
            client1.ConnectAsync("Player1"),
            client2.ConnectAsync("Player2"), 
            client3.ConnectAsync("Player3")
        );
        
        // Act - Concurrent transitions
        var transitions = Task.WhenAll(
            client1.PerformZoneTransition(new GridSquare(1, 0)),
            client2.PerformZoneTransition(new GridSquare(1, 0)),
            client3.PerformZoneTransition(new GridSquare(1, 0))
        );
        
        // Should complete without deadlock or interference
        await transitions.WaitAsync(TimeSpan.FromSeconds(10));
        
        // Assert - All should be connected to target zone
        Assert.True(client1.CurrentZone?.Equals(new GridSquare(1, 0)));
        Assert.True(client2.CurrentZone?.Equals(new GridSquare(1, 0)));
        Assert.True(client3.CurrentZone?.Equals(new GridSquare(1, 0)));
    }
}
```

---

## 🤖 Automated End-to-End Testing

### Bot-Based Testing

#### Zone Boundary Testing Bot
```csharp
public class ZoneBoundaryTestBot : AutoMoveController
{
    private readonly List<Vector2> _boundaryTestPoints;
    private int _currentTargetIndex = 0;
    
    public ZoneBoundaryTestBot()
    {
        // Define points near zone boundaries for testing
        _boundaryTestPoints = new List<Vector2>
        {
            new Vector2(498, 250),  // Just before (1,0) boundary
            new Vector2(502, 250),  // Just after (1,0) boundary  
            new Vector2(250, 498),  // Just before (0,1) boundary
            new Vector2(250, 502),  // Just after (0,1) boundary
        };
    }
    
    public override (Vector2? move, Vector2? shoot) Update(WorldState world, List<GridSquare> zones, Vector2 position)
    {
        var target = _boundaryTestPoints[_currentTargetIndex];
        var direction = Vector2.Normalize(target - position);
        
        // Move to next target when close enough
        if (Vector2.Distance(position, target) < 10f)
        {
            _currentTargetIndex = (_currentTargetIndex + 1) % _boundaryTestPoints.Count;
        }
        
        return (direction * 60f, null); // Move toward target
    }
}
```

#### Stress Testing Bot
```csharp
public class StressTestBot : AutoMoveController
{
    private readonly Random _random = new Random();
    
    public override (Vector2? move, Vector2? shoot) Update(WorldState world, List<GridSquare> zones, Vector2 position)
    {
        // Random movement to generate unpredictable transition patterns
        var randomDirection = new Vector2(
            _random.NextSingle() * 2 - 1,
            _random.NextSingle() * 2 - 1
        );
        
        return (Vector2.Normalize(randomDirection) * 60f, null);
    }
}
```

### Automated Test Scenarios

#### Scenario 1: Boundary Oscillation Prevention
```bash
#!/bin/bash
# Test: Verify debouncer prevents boundary oscillation

echo "Starting boundary oscillation test..."

# Start bot with boundary testing behavior
dotnet run --project Shooter.Bot -- --BotController BoundaryTest &
BOT_PID=$!

# Monitor for 5 minutes
sleep 300

# Analyze results
echo "Analyzing boundary behavior..."
OSCILLATIONS=$(grep "Ship moved to zone" logs/bot-0.log | tail -100 | \
    awk '{print $NF}' | uniq -c | awk '$1 > 5 {count++} END {print count+0}')

if [ $OSCILLATIONS -gt 3 ]; then
    echo "FAIL: Detected $OSCILLATIONS potential oscillation patterns"
    exit 1
else
    echo "PASS: No excessive oscillation detected"
fi

kill $BOT_PID
```

#### Scenario 2: Performance Under Load  
```bash
#!/bin/bash
# Test: Multiple bots with concurrent transitions

echo "Starting load test with 5 concurrent bots..."

# Start multiple bots
for i in {1..5}; do
    dotnet run --project Shooter.Bot -- --BotName "LoadBot$i" &
    PIDS[$i]=$!
done

# Run for 10 minutes
sleep 600

# Collect performance metrics
echo "Collecting performance metrics..."
TOTAL_SUCCESS=$(grep "Successfully connected to zone" logs/*.log | wc -l)
TOTAL_FORCED=$(grep "FORCING transition after" logs/*.log | wc -l)
TOTAL_ATTEMPTS=$((TOTAL_SUCCESS + TOTAL_FORCED))

if [ $TOTAL_ATTEMPTS -gt 0 ]; then
    SUCCESS_RATE=$(echo "scale=1; $TOTAL_SUCCESS * 100 / $TOTAL_ATTEMPTS" | bc)
    echo "Load Test Results: $SUCCESS_RATE% success rate ($TOTAL_SUCCESS/$TOTAL_ATTEMPTS)"
    
    if [ $(echo "$SUCCESS_RATE < 70" | bc) -eq 1 ]; then
        echo "FAIL: Success rate below 70% under load"
        exit 1
    else
        echo "PASS: Acceptable performance under load"
    fi
else
    echo "FAIL: No transition attempts detected"
    exit 1
fi

# Cleanup
for pid in "${PIDS[@]}"; do
    kill $pid 2>/dev/null
done
```

---

## 📊 Test Data Collection and Analysis

### Automated Test Reporting

#### Test Results Dashboard Script
```bash
#!/bin/bash
# Generate comprehensive test report

REPORT_FILE="test-report-$(date +%Y%m%d_%H%M%S).html"

cat > $REPORT_FILE << EOF
<!DOCTYPE html>
<html>
<head>
    <title>Zone Transition Test Report</title>
    <style>
        body { font-family: Arial, sans-serif; margin: 20px; }
        .pass { color: green; font-weight: bold; }
        .fail { color: red; font-weight: bold; }
        .metric { background: #f5f5f5; padding: 10px; margin: 10px 0; }
        table { border-collapse: collapse; width: 100%; }
        th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }
        th { background-color: #f2f2f2; }
    </style>
</head>
<body>
    <h1>Zone Transition Test Report</h1>
    <p>Generated: $(date)</p>
    
    <h2>Test Summary</h2>
EOF

# Run unit tests and capture results
dotnet test --logger "console;verbosity=normal" > test-output.txt 2>&1
UNIT_TEST_EXIT=$?

if [ $UNIT_TEST_EXIT -eq 0 ]; then
    echo '<p class="pass">Unit Tests: PASSED</p>' >> $REPORT_FILE
else
    echo '<p class="fail">Unit Tests: FAILED</p>' >> $REPORT_FILE
fi

# Performance metrics
echo "<h2>Performance Metrics</h2>" >> $REPORT_FILE
RECENT_SUCCESS=$(grep "Successfully connected to zone" logs/bot-0.log | tail -50 | wc -l)  
RECENT_FORCED=$(grep "FORCING transition after" logs/bot-0.log | tail -50 | wc -l)
TOTAL_RECENT=$((RECENT_SUCCESS + RECENT_FORCED))

if [ $TOTAL_RECENT -gt 0 ]; then
    SUCCESS_RATE=$(echo "scale=1; $RECENT_SUCCESS * 100 / $TOTAL_RECENT" | bc)
    echo "<div class='metric'>Success Rate: $SUCCESS_RATE% ($RECENT_SUCCESS/$TOTAL_RECENT)</div>" >> $REPORT_FILE
fi

# Add more metrics...
echo "</body></html>" >> $REPORT_FILE

echo "Test report generated: $REPORT_FILE"
```

### Performance Regression Testing

#### Automated Performance Comparison
```csharp
public class PerformanceRegressionTests
{
    [Fact]
    public async Task ZoneTransition_PerformanceRegression_ShouldMaintainBaseline()
    {
        // Arrange
        const int TestTransitions = 100;
        const double MaxAverageTime = 2.0; // seconds
        const double MinSuccessRate = 0.8; // 80%
        
        var results = new List<(bool success, double duration)>();
        
        // Act - Perform many transitions
        for (int i = 0; i < TestTransitions; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            var success = false;
            
            try
            {
                await _testClient.PerformZoneTransition(new GridSquare(i % 2, 0));
                success = true;
            }
            catch (Exception)
            {
                success = false;
            }
            finally
            {
                stopwatch.Stop();
                results.Add((success, stopwatch.Elapsed.TotalSeconds));
            }
        }
        
        // Assert - Verify performance metrics
        var successCount = results.Count(r => r.success);
        var successRate = (double)successCount / TestTransitions;
        var averageTime = results.Where(r => r.success).Average(r => r.duration);
        
        Assert.True(successRate >= MinSuccessRate, 
            $"Success rate {successRate:P} below baseline {MinSuccessRate:P}");
        Assert.True(averageTime <= MaxAverageTime,
            $"Average time {averageTime:F2}s above baseline {MaxAverageTime:F2}s");
            
        // Log metrics for trending
        _logger.LogInformation("Performance Test: Success Rate {SuccessRate:P}, Average Time {AverageTime:F2}s",
            successRate, averageTime);
    }
}
```

---

## 🔄 Continuous Integration Testing

### CI Pipeline Configuration

#### GitHub Actions Example
```yaml
name: Zone Transition Tests

on: [push, pull_request]

jobs:
  unit-tests:
    runs-on: ubuntu-latest
    steps:
    - uses: actions/checkout@v2
    - name: Setup .NET
      uses: actions/setup-dotnet@v1
      with:
        dotnet-version: 9.0.x
    - name: Run Unit Tests
      run: dotnet test --configuration Release --logger trx --results-directory TestResults
    - name: Publish Test Results
      uses: dorny/test-reporter@v1
      if: always()
      with:
        name: Unit Test Results
        path: TestResults/*.trx
        reporter: dotnet-trx

  integration-tests:
    runs-on: ubuntu-latest
    needs: unit-tests
    steps:
    - uses: actions/checkout@v2
    - name: Setup .NET
      uses: actions/setup-dotnet@v1
    - name: Start Test Environment
      run: |
        cd granville/samples/Rpc
        docker-compose -f docker-compose.test.yml up -d
    - name: Run Integration Tests
      run: dotnet test --configuration Release --filter "Category=Integration"
    - name: Performance Regression Test
      run: ./scripts/performance-regression-test.sh

  e2e-tests:
    runs-on: ubuntu-latest
    needs: integration-tests
    steps:
    - name: Run Bot Boundary Tests
      run: ./scripts/boundary-test.sh
    - name: Run Load Tests
      run: ./scripts/load-test.sh
    - name: Analyze Results
      run: ./scripts/analyze-test-results.sh
```

This comprehensive testing strategy ensures zone transition functionality remains reliable while preventing performance regressions and catching edge cases early in the development cycle.