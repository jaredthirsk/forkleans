# Shooter Game Critical Issues

This directory contains documentation for critical issues discovered during testing of the Shooter RPC sample application.

## Issue Priority

### 🔴 CRITICAL - Must Fix
1. **[Zone Transition Deadlock](./zone-transition-deadlock/README.md)**
   - Players get stuck between zones for 28+ seconds
   - Game becomes unplayable when crossing zone boundaries
   - Root cause of many other issues

### 🟠 HIGH - Should Fix Soon  
2. **[Bot Connection Failures](./bot-connection-failures/README.md)**
   - Bots cannot connect to the game
   - HTTP connection refused errors
   - Blocks automated testing

### 🟡 MEDIUM - Fix After Root Causes
3. **[SignalR Connection Closures](./signalr-connection-closures/README.md)**
   - Symptom of other issues
   - Chat functionality breaks
   - Should resolve after fixing #1 and #2

## Quick Summary

All three issues are interconnected:
- Zone transition deadlock causes clients to lose connection
- Bot connection failures prevent testing
- SignalR closures are symptoms of the above issues

## Recommended Fix Order
1. Fix zone transition deadlock first (root cause)
2. Fix bot connection issues (enables testing)  
3. Verify SignalR issues resolve themselves
4. Add resilience/reconnection logic as needed

## Testing Configuration
- **Environment**: .NET Aspire orchestrated
- **Ports**: 
  - Silo: 7071, 7080-7081
  - ActionServers: 7072-7075
  - RPC: 12000-12003
- **Transport**: LiteNetLib (UDP)
- **Date Discovered**: 2025-09-12

## Monitoring
Use the monitoring script to detect new errors:
```bash
./scripts/monitor-for-errors.sh
```

The script watches log files and alerts when errors occur (ignoring development warnings).