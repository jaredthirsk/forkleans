# Zone Transition Debugging Guide

## Overview
Zone transitions involve multiple components and can have delays at various points. This guide helps diagnose where delays occur.

## Key Components in Zone Transition

1. **Client Detection** (ForkleansRpcGameClientService)
   - Polls world state every 16ms
   - Detects when player is within 50 units of zone boundary
   - Throttles boundary checks to once per second

2. **Server Detection** (GameService)
   - CheckForBoundaryTransfers runs every 500ms
   - WorldSimulation.GetPlayersOutsideZone() identifies players needing transfer

3. **Transition Process**
   ```
   Client detects boundary → HTTP request to Silo → Silo returns new server info → 
   Client disconnects from old RPC → Client connects to new RPC → 
   Server transfers entity data → Client resumes polling
   ```

## Common Delay Points

### 1. Boundary Detection Delay (0-1000ms)
- Client only checks boundaries once per second
- Solution: Reduce throttle time in PollWorldState

### 2. Server-Side Detection (0-500ms)  
- GameService only checks every 500ms
- Solution: Reduce CheckForBoundaryTransfers interval

### 3. HTTP Request to Silo (Variable)
- Network latency to Orleans Silo
- Silo processing time to determine correct server

### 4. RPC Disconnection/Reconnection (2000ms+)
- Fixed 2000ms delay for handshake in ConnectToActionServer
- UDP connection establishment
- Manifest exchange between client and server

### 5. Entity Transfer
- Server must serialize and transfer entity state
- New server must acknowledge receipt

## Debugging Steps

1. **Enable Zone Transition Logs**
   - Look for logs with `[ZONE_TRANSITION]` prefix
   - These show timing at each step

2. **Check Server Logs**
   - Look for `[ZONE_TRANSITION_SERVER]` logs
   - Shows server-side transfer timing

3. **Monitor Debug Info**
   - The UI shows when player zone doesn't match server zone
   - Includes timestamp to measure total delay

4. **Common Issues**
   - Long RPC handshake delay (hardcoded 2000ms wait)
   - Polling intervals causing detection delays
   - Network latency between services

## Optimization Suggestions

1. **Reduce Detection Intervals**
   - Client boundary check: 1s → 100ms
   - Server transfer check: 500ms → 100ms

2. **Optimize RPC Connection**
   - Reduce handshake delay from 2000ms
   - Pre-establish connections to nearby servers
   - Keep connection pool warm

3. **Predictive Transfers**
   - Start transfer process before crossing boundary
   - Based on velocity and direction

4. **State Caching**
   - Cache player state during transitions
   - Interpolate position during connection switch