# Shooter Integration Tests

This project contains integration tests for the Shooter RPC sample, focusing on the chat system and game flow mechanics.

## Quick Start

From the test directory (`/samples/Rpc/test`):

```bash
# Run simple tests (no services required)
./run-tests.sh --simple

# View all test options
./run-tests.sh --help
```

**Note:** Full integration tests require manual service startup due to Aspire AppHost complexity. See TESTING_GUIDE.md for details.

## Overview

The tests use Aspire's testing framework to start the complete Shooter application (Silo, ActionServers, and Bots) and verify behavior through log analysis.

## Test Categories

### Chat System Tests
- Verifies victory chat messages are broadcast to all bots
- Tests game restart messages
- Validates player score announcements
- Ensures messages propagate across all zones

### Game Flow Tests
- Validates victory condition detection
- Tests automatic game restart after victory
- Verifies bot targeting priorities (enemies > asteroids)
- Ensures predictable bot movement in test mode

## Running the Tests

```bash
# From the Shooter.Tests directory
dotnet test

# Run with detailed output
dotnet test -v n

# Run a specific test
dotnet test --filter "FullyQualifiedName~ChatSystemTests.Bots_Should_Receive_Victory"

# Run with test logging
dotnet test --logger "console;verbosity=detailed"
```

## Test Configuration

The `appsettings.Test.json` file controls test parameters:
- `BotCount`: Number of bot instances (default: 3)
- `ActionServerCount`: Number of action servers (default: 2)
- `TestTimeout`: Maximum time for tests in seconds (default: 120)
- `TestMode`: Enables deterministic bot behavior (default: true)

## Log Analysis

Tests analyze log files written to `logs/test/{test-run-id}/`:
- `silo.log` - Orleans silo logs
- `actionserver-{n}.log` - Action server logs
- `bot-{n}.log` - Bot client logs

The `LogAnalyzer` utility provides grep-like functionality:
- Wait for specific log entries
- Extract structured data (e.g., chat messages)
- Count pattern occurrences
- Verify message ordering

## Troubleshooting

### Tests Timing Out
- Increase `TestTimeout` in appsettings.Test.json
- Check if services are starting properly
- Verify no port conflicts

### Missing Log Entries
- Ensure log levels are set appropriately
- Check log file paths match expected patterns
- Verify services are writing to the correct directory

### Flaky Tests
- Bot behavior should be deterministic in test mode
- Consider adding longer delays for service initialization
- Check for race conditions in log writing

## Adding New Tests

1. Create test class inheriting from `IClassFixture<ShooterTestFixture>`
2. Use `LogAnalyzer` to wait for and verify log entries
3. Follow existing patterns for timeout handling
4. Use FluentAssertions for readable assertions

Example:
```csharp
[Fact]
public async Task My_New_Test()
{
    // Wait for a specific log entry
    var entry = await _logAnalyzer.WaitForLogEntry(
        "bot-0.log",
        "Expected message pattern",
        TimeSpan.FromSeconds(30));
        
    entry.Should().NotBeNull("reason for expectation");
}
```