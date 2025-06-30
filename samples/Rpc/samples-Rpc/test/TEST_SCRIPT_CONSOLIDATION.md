# Test Script Consolidation Summary

## What Was Done

Consolidated multiple test runner scripts to avoid confusion:

### Scripts Removed
- `/samples/Rpc/Shooter.Tests/run-tests.sh` - Duplicate functionality

### Scripts Kept
1. **`/samples/Rpc/run-tests.sh`** - Main test runner for Shooter sample tests
   - Enhanced with `--simple` flag for unit tests
   - Runs from the samples/Rpc directory
   - Handles all Shooter.Tests functionality

2. **`/test/Rpc/run-test.sh`** - Orleans RPC integration tests
   - Different purpose: runs server/client integration tests
   - Not related to Shooter sample

## Usage

### Running Shooter Tests
From `/samples/Rpc` directory:

```bash
# Run simple unit tests (recommended)
./run-tests.sh --simple

# Run all tests (integration tests may timeout)
./run-tests.sh

# Run specific tests
./run-tests.sh --filter "TestName"

# View help
./run-tests.sh --help
```

### Running Orleans RPC Tests
From `/test/Rpc` directory:

```bash
# Run server
./run-test.sh server

# Run client
./run-test.sh client

# Run combined test
./run-test.sh combined
```

## Benefits

1. **Less Confusion** - Only one script per purpose
2. **Consistent Location** - Test scripts in their logical parent directories
3. **Clear Documentation** - Updated all references to point to correct scripts
4. **Enhanced Functionality** - Added `--simple` flag for quick unit test runs