# Bot Integration Testing Guide

This guide documents the process of testing the Shooter sample with the automated bot client, including startup procedures, timing expectations, and troubleshooting steps.

## Test Started: 2025-07-23 20:24

## Quick Start

```bash
cd /mnt/c/forks/orleans/granville/samples/Rpc/Shooter.AppHost
./rl.sh
```

This script:
1. Kills existing Shooter processes (via `k.sh`)
2. Deletes old logs from `../logs/`
3. Cleans the project
4. Runs the AppHost in Release mode

## Expected Startup Sequence

### Phase 1: AppHost Initialization
- **Duration**: ~2-3 seconds
- **What happens**: 
  - Aspire dashboard starts on port 20032
  - Service discovery begins
  - Projects start building in parallel

### Phase 2: Service Startup
- **Duration**: ~5-8 seconds
- **Order**:
  1. Orleans Silo (ports: HTTP 7071/7073, Orleans 11111/30000) - ~2s
  2. ActionServers 0-3 (HTTP ports 43010-43013, RPC ports 12000-12003) - ~8s each
  3. Bot client (after 10s configured delay) - ~15s from start

### Phase 3: Bot Connection
- **Expected delay**: 10 seconds (configured in OrleansStartupDelayService)
- **Duration**: ~15-20 seconds from AppHost start
- **Success indicators**: 
  - Bot logs: "Bot LiteNetLibTest0 connecting to game..."
  - Bot registers with unique ID
  - ActionServer assigns bot to zone

## Log Locations

- **Silo logs**: `../logs/silo*.log`
- **ActionServer logs**: `../logs/actionserver*.log`
- **Bot logs**: `../logs/bot*.log`
- **Centralized logs**: `../logs/` directory:
  - `silo.log`, `silo-console.log`
  - `actionserver-0.log` through `actionserver-3.log`
  - `bot-0.log`, `bot-0-console.log`

## Common Issues and Solutions

### Issue 1: Port Conflicts
- **Error**: "Failed to bind to address http://127.0.0.1:20032: address already in use"
- **Solution**: Kill existing processes with `./rl.sh` or manually
- **Prevention**: Always use `./rl.sh` which includes cleanup

### Issue 2: RPC Serialization Error
- **Error**: `UnsupportedWireTypeException: A WireType value of LengthPrefixed is expected`
- **Cause**: Client expects wrong wire type when deserializing server response
- **Status**: Under investigation - appears to be related to marker byte handling
- **Details**:
  - Server successfully sends response with Orleans binary marker (0x00)
  - Client fails to deserialize the response
  - Server logs show playerId being received as null 

## Testing Checklist

- [ ] All services start without errors
- [ ] Bot connects successfully
- [ ] No serialization errors in logs
- [ ] Bot movement/actions visible in logs
- [ ] No connection drops or retries
- [ ] Memory usage stable
- [ ] CPU usage reasonable

## Performance Metrics

- **Build time**: ~5-8 seconds (parallel build via Aspire)
- **Total startup time**: ~10 seconds for all services
- **Time to bot connection**: ~15-20 seconds from AppHost start
- **Network traffic**: ActionServers show steady packet flow even when idle

## Notes

### Key Findings (2025-07-23)
1. Bot successfully connects to Orleans and gets assigned to ActionServer
2. RPC handshake completes successfully between bot and ActionServer
3. ConnectPlayer RPC call fails due to serialization issue
4. Server receives playerId as null instead of the actual GUID
5. Response serialization works (server sends "FAILED") but client fails to deserialize

### Next Steps
1. Fix RPC argument serialization to properly handle string arguments
2. Investigate why client-side deserialization expects wrong wire type
3. Test with updated RPC libraries once fixes are applied