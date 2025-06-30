# Test Scripts Overview

This directory contains several test-related scripts. Here's what each one does and when to use them:

## Primary Test Scripts

### `run-tests.sh`
**Purpose:** Run xUnit tests from the Shooter.Tests project  
**When to use:** For automated unit and integration testing  
**Usage:**
```bash
./run-tests.sh --simple    # Run simple unit tests only
./run-tests.sh            # Run all tests (integration tests may timeout)
./run-tests.sh --help     # Show all options
```

### `manual-integration-test.sh`
**Purpose:** Manually start services and check for common errors  
**When to use:** When xUnit integration tests timeout or for debugging  
**Usage:**
```bash
./manual-integration-test.sh               # Start Silo and ActionServer
./manual-integration-test.sh --with-bot    # Also start a Bot client
./manual-integration-test.sh --help        # Show all options
```

## Legacy/Redundant Scripts (To Be Removed)

### `test-shooter.sh` (DEPRECATED)
- Starts Silo and ActionServer
- Checks for specific registration errors
- **Replaced by:** `manual-integration-test.sh`

### `test-rpc.sh` (DEPRECATED)
- Starts Silo, ActionServer, and Bot
- Checks logs for errors
- **Replaced by:** `manual-integration-test.sh --with-bot`

## Helper Scripts

### `kill-shooter-processes.sh`
**Purpose:** Kill all running Shooter processes  
**When to use:** Clean up after tests or before starting fresh

### `show-shooter-processes.sh`
**Purpose:** Show all running Shooter processes with details  
**When to use:** Debug which services are running

## Recommendations

1. **For automated testing:** Use `run-tests.sh --simple`
2. **For manual testing:** Use `manual-integration-test.sh`
3. **For cleanup:** Use `kill-shooter-processes.sh`
4. **Remove deprecated scripts:** `test-shooter.sh` and `test-rpc.sh` should be deleted

## Test Workflow

1. Clean up old processes:
   ```bash
   ./kill-shooter-processes.sh
   ```

2. Run unit tests:
   ```bash
   ./run-tests.sh --simple
   ```

3. For integration testing (if xUnit tests timeout):
   ```bash
   ./manual-integration-test.sh --with-bot
   # In another terminal:
   tail -f logs/*.log
   ```

4. Clean up when done:
   ```bash
   ./kill-shooter-processes.sh
   ```