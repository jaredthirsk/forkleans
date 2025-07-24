# Bot Integration Testing Guide

This guide documents the process of testing the Shooter sample with the automated bot client, including startup procedures, timing expectations, and troubleshooting steps.

## Test Started: [TIME WILL BE FILLED IN]

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
- **Duration**: [TO BE MEASURED]
- **What happens**: 
  - Aspire dashboard starts
  - Service discovery begins
  - Projects start building

### Phase 2: Service Startup
- **Duration**: [TO BE MEASURED]
- **Order**:
  1. Orleans Silo (port 7071)
  2. ActionServers 1-4 (ports 7072-7075)
  3. Bot client (after delay)

### Phase 3: Bot Connection
- **Expected delay**: Bot has a configured startup delay
- **Duration**: [TO BE MEASURED]
- **Success indicators**: [TO BE DOCUMENTED]

## Log Locations

- **Silo logs**: `../logs/silo*.log`
- **ActionServer logs**: `../logs/actionserver*.log`
- **Bot logs**: `../logs/bot*.log`
- **Alternative locations**: 
  - `Shooter.Silo/logs/`
  - `Shooter.ActionServer/logs/`
  - `Shooter.Bot/logs/`

## Common Issues and Solutions

### Issue 1: [TO BE DOCUMENTED]
- **Error**: 
- **Solution**: 
- **Prevention**: 

## Testing Checklist

- [ ] All services start without errors
- [ ] Bot connects successfully
- [ ] No serialization errors in logs
- [ ] Bot movement/actions visible in logs
- [ ] No connection drops or retries
- [ ] Memory usage stable
- [ ] CPU usage reasonable

## Performance Metrics

- **Build time**: [TO BE MEASURED]
- **Total startup time**: [TO BE MEASURED]
- **Time to bot connection**: [TO BE MEASURED]
- **Memory usage**:
  - Silo: [TO BE MEASURED]
  - ActionServers: [TO BE MEASURED]
  - Bot: [TO BE MEASURED]

## Notes

[This section will be updated as testing progresses]