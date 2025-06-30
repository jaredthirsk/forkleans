# Shooter Integration Testing Guide

## Overview

This test project contains integration tests for the Shooter RPC sample. The tests verify the chat system functionality and game flow mechanics by analyzing log files produced by the running services.

## Test Structure

### Unit Tests
Simple unit tests that don't require services:
- `SimpleLogTest` - Tests log parsing and regex patterns

### Integration Tests
Full integration tests that require running services:
- `ChatSystemTests` - Verifies chat message distribution
- `GameFlowTests` - Tests victory conditions and game mechanics

## Running Tests

### Simple Tests (No Services Required)
From the test directory (`/samples/Rpc/test`):
```bash
./run-tests.sh --simple
```

### Full Integration Tests (Manual Setup)

Due to the complexity of starting the full Aspire AppHost programmatically, the integration tests currently require manual service startup:

#### Step 1: Start Services Manually

Open multiple terminals and run:

**Terminal 1 - Silo:**
```bash
cd Shooter.Silo
dotnet run
```

**Terminal 2 - ActionServer 1:**
```bash
cd Shooter.ActionServer
dotnet run --urls http://localhost:7072
```

**Terminal 3 - ActionServer 2:**
```bash
cd Shooter.ActionServer
dotnet run --urls http://localhost:7073
```

**Terminal 4 - Bot 1:**
```bash
cd Shooter.Bot
dotnet run
```

**Terminal 5 - Bot 2:**
```bash
cd Shooter.Bot
ASPIRE_INSTANCE_ID=1 dotnet run
```

**Terminal 6 - Bot 3:**
```bash
cd Shooter.Bot
ASPIRE_INSTANCE_ID=2 dotnet run
```

#### Step 2: Monitor Logs

In another terminal, monitor the logs:
```bash
tail -f logs/*.log | grep -E "chat|victory|Victory|Chat"
```

#### Step 3: Verify Expected Behavior

1. **Bots Connect**: Look for "connected as player" in bot logs
2. **Combat Activity**: Enemies are destroyed
3. **Victory Detection**: "Victory condition met" in silo.log
4. **Chat Broadcast**: "Broadcasting chat message from Game System: 🎉 Victory!" in silo.log
5. **Bot Reception**: "Received chat message from Game System: 🎉 Victory!" in each bot log
6. **Game Restart**: After 15 seconds, "Game restarted" message

### Using Aspire AppHost (Recommended)

The easiest way to run all services:

```bash
cd Shooter.AppHost
dotnet run
```

Then monitor logs:
```bash
watch -n 1 'ls -la logs/*.log'
tail -f logs/*.log
```

## Test Implementation Details

### ShooterTestFixture
Attempts to start the Aspire AppHost programmatically but currently has timeout issues due to:
- Service startup time
- Complex orchestration
- Log file path resolution

### LogAnalyzer
Provides utilities for:
- Waiting for specific log entries
- Extracting structured data from logs
- Pattern matching with regex support
- Counting occurrences across files

## Future Improvements

1. **Optimize Service Startup**: Reduce startup time for faster tests
2. **Mock Services**: Create lightweight mocks for testing
3. **Direct API Testing**: Test RPC endpoints directly without full simulation
4. **Docker Compose**: Use containers for reproducible test environment
5. **Log Streaming**: Real-time log analysis instead of file polling

## Troubleshooting

### Tests Timing Out
- Increase timeout in appsettings.Test.json
- Ensure all services start successfully
- Check for port conflicts (7071-7073)

### Missing Log Files
- Verify services are writing to `../logs/` relative to their directory
- Check file permissions
- Ensure log directory exists

### Chat Messages Not Received
- Verify Silo has started successfully
- Check ActionServer zone assignments
- Ensure bots are connected to ActionServers
- Look for RPC connection errors in logs

## Manual Verification Script

```bash
#!/bin/bash
# Quick verification of chat system

echo "Checking for victory messages..."
grep -h "Victory" logs/*.log | tail -10

echo -e "\nChecking for chat broadcasts..."
grep -h "Broadcasting chat" logs/silo.log | tail -5

echo -e "\nChecking bot reception..."
for i in 0 1 2; do
    echo "Bot $i:"
    grep -h "Received chat" logs/bot-$i.log | tail -3
done
```