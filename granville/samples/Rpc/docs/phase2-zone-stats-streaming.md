# Phase 2: Zone Stats Streaming Implementation Plan

## Overview
Convert zone statistics from polling to IAsyncEnumerable streaming pattern.

## Current State
- Clients poll ActionServers for zone stats
- No global view of all zones
- Inefficient periodic requests

## Target Architecture
```
ActionServers → Orleans → WorldManagerGrain
                              ↓
                    IAsyncEnumerable<GlobalZoneStats>
                              ↓
                         SignalR Hub → Internet Clients
```

## Implementation Steps

### 1. Add Zone Stats Reporting to ActionServers
- ActionServers push stats to WorldManagerGrain periodically
- Use Orleans grain method calls (secure internal network)

### 2. Add IAsyncEnumerable to IWorldManagerGrain
```csharp
public interface IWorldManagerGrain : IGrainWithIntegerKey
{
    // Existing methods...
    
    // New streaming method
    IAsyncEnumerable<GlobalZoneStats> StreamZoneStatistics(
        TimeSpan updateInterval,
        [EnumeratorCancellation] CancellationToken cancellationToken);
}
```

### 3. Implement Stats Aggregation
- WorldManagerGrain maintains current stats from all zones
- Yields aggregated updates at requested interval
- Handles zone additions/removals dynamically

### 4. Add SignalR Endpoint (Future)
- Silo exposes SignalR hub
- Consumes IAsyncEnumerable stream
- Broadcasts to connected clients

## Benefits
- Real-time global view of game state
- Efficient server-push model
- Clients control update rate
- Natural backpressure handling