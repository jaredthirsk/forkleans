# Integration Test Guide

## Why Integration Tests Were Timing Out

The integration tests were timing out because:

1. **Infinite Game Loop** - The game runs forever by default, so tests waiting for completion never finish
2. **Port Conflicts** - Multiple test runs or existing processes cause port binding failures
3. **Complex Service Startup** - Starting all services through Aspire takes time and coordination
4. **No Clear "Ready" Signal** - Tests had to guess when services were ready

## The Solution: QuitAfterNRounds

We've added a `QuitAfterNRounds` configuration parameter to the WorldManagerGrain:

- **Default (0)**: Game runs forever (normal operation)
- **Integration Tests (2)**: Game quits after 2 rounds complete

### How It Works

1. When a game round ends (all enemies defeated), the WorldManagerGrain increments a counter
2. If the counter reaches the configured limit, the server:
   - Sends a farewell chat message
   - Gracefully shuts down the application
3. This allows integration tests to complete naturally

### Configuration

- `appsettings.json`: Default settings (QuitAfterNRounds = 0)
- `appsettings.IntegrationTest.json`: Test settings (QuitAfterNRounds = 2)

The test fixture automatically sets `ASPNETCORE_ENVIRONMENT=IntegrationTest` to use the test configuration.

## Port Conflict Resolution

The test fixture now:

1. **Kills Existing Processes** - Runs kill-shooter-processes.sh before starting
2. **Random Dashboard Port** - Uses a random port (20000-29000) for Aspire dashboard
3. **Environment Isolation** - Each test run gets a unique test ID

## Running Integration Tests

### Simple Tests (No Services)
```bash
./run-tests.sh --simple
```

### Full Integration Tests
```bash
./run-tests.sh
```

The integration tests will now:
1. Start all services with IntegrationTest configuration
2. Run through 2 game rounds
3. Automatically shut down
4. Complete within the 5-minute timeout

## Troubleshooting

### Tests Still Timing Out?
- Check if services are starting properly in the logs
- Verify no other instances are running: `scripts/show-shooter-processes.sh`
- Increase TestTimeout in appsettings.Test.json if needed

### Port Conflicts?
- Run `scripts/kill-shooter-processes.sh` before tests
- Check for other applications using ports 7071-7073, 5000

### DLL Access Errors?
- These occur when multiple builds run simultaneously
- Ensure only one test run is active
- Clean and rebuild if persistent: `dotnet clean && dotnet build`