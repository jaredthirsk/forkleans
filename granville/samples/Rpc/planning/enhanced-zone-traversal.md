# Enhanced Zone Traversal Planning

## Problem Statement

Players experience disruption when moving near zone boundaries due to rapid oscillation between zones. This causes:
- Network overhead from repeated reconnections
- Visual glitches and lag
- Server-client disagreements about player location
- Poor user experience when moving along boundaries

## Current Implementation

- Grid squares are 500x500 world units
- Zone detection uses simple `Math.Floor(position / 500)`
- No hysteresis or dead zone around boundaries
- Immediate zone transition on boundary crossing

## Solution Approaches

### 1. ✅ One-Way Hysteresis (SELECTED FOR INITIAL IMPLEMENTATION)

**Description:** Prevent returning to the previous zone unless the player moves sufficiently far (30-50 units) into it.

**Implementation:**
```csharp
private GridSquare? _previousZone;
private float REENTRY_THRESHOLD = 30f; // Must move 30 units into previous zone

if (newZone == _previousZone && distanceFromBoundary < REENTRY_THRESHOLD)
{
    // Stay in current zone, apply bouncy wall physics
    return currentZone;
}
```

**Client-Side Bouncy Wall:**
- When blocked from re-entering previous zone:
  - Reverse velocity component perpendicular to boundary
  - Preserve velocity component parallel to boundary
  - Visual feedback: colored boundary with directional chevrons

**Visual Indicators:**
- Color the boundary edge player came from (e.g., red tint)
- Add animated chevron arrows pointing away from blocked zone
- Fade effect over time as player moves away

**Pros:**
- Simple to implement
- Prevents ping-ponging
- Clear visual feedback
- Maintains game state integrity

**Cons:**
- Could feel "sticky" at boundaries
- Requires client-side physics adjustment

---

### 2. Server-Authoritative Zone Assignment

**Description:** Server is sole authority on zone assignments with built-in hysteresis.

**Implementation:**
- Server tracks player position history
- Server decides zone transitions
- Client accepts server decisions
- Server implements smart hysteresis logic

**Pros:**
- Single source of truth
- No client-server disagreements
- Cheat-proof

**Cons:**
- Increased server CPU load
- Potential lag in zone updates
- Client prediction complexity

---

### 3. 🌟 Boundary Overlap Zones (FUTURE DELUXE IMPLEMENTATION)

**Description:** Create overlapping regions where both adjacent servers handle the player.

**Architecture:**
```
Zone A [0-500]
       [475-525] <- 50-unit overlap region
              Zone B [500-1000]
```

**Implementation:**
- Within 25 units of boundary: both zones process player
- Primary zone (where player is deeper) handles authoritative state
- Secondary zone provides read-only visibility
- Seamless handoff when crossing center line

**Advanced Features:**
- Cross-zone entity interpolation
- Predictive entity spawning in overlap regions
- Smooth LOD (Level of Detail) transitions
- Zero-latency zone transitions

**Pros:**
- Seamless transitions
- No connection switching needed
- Best user experience
- Natural entity visibility

**Cons:**
- Complex implementation
- Duplicate processing overhead
- Synchronization challenges
- Requires careful state management

---

### 4. Predictive Pre-Connection

**Description:** Maintain persistent connections to all adjacent zones.

**Implementation:**
- Pre-establish connections to 8 neighboring zones
- Keep connections warm with periodic heartbeats
- Instant switching when crossing boundaries
- Connection pooling and recycling

**Pros:**
- Near-instant transitions
- No reconnection overhead
- Smooth experience

**Cons:**
- 9x connection overhead per client
- Resource intensive
- Connection management complexity

---

### 5. Movement Vector-Based Prediction (REJECTED)

**Description:** Use velocity to determine genuine zone entry intent.

**Why Rejected:**
- Adds unnecessary complexity
- Bouncy wall already provides direction enforcement
- Could interfere with tactical retreating
- One-Way Hysteresis is simpler and sufficient

---

### 6. Time-Based Cooldown (REJECTED)

**Description:** Prevent zone changes within X seconds of last transition.

**Why Rejected:**
- Could trap player in wrong zone
- Feels unresponsive
- Doesn't address root cause
- Poor user experience

---

### 7. Committed Zone Transitions (REJECTED)

**Description:** Require staying in new zone for minimum time to complete transition.

**Why Rejected:**
- Adds unnecessary delay
- Complex state management
- Confusing for players
- One-Way Hysteresis is superior

---

## Implementation Roadmap

### Phase 1: One-Way Hysteresis (Current)
- [x] Identify zone oscillation issue
- [ ] Implement one-way zone blocking logic
- [ ] Add bouncy wall physics on client
- [ ] Add visual indicators (colored borders, chevrons)
- [ ] Test with multiple players
- [ ] Tune threshold distances

### Phase 2: Optimization
- [ ] Add server-side validation
- [ ] Implement zone transition events
- [ ] Add metrics/telemetry
- [ ] Performance profiling
- [ ] Optimize network traffic

### Phase 3: Boundary Overlap Zones (Future)
- [ ] Design overlap region protocol
- [ ] Implement dual-zone entity management
- [ ] Add cross-zone state synchronization
- [ ] Implement smooth handoff logic
- [ ] Add interpolation for entities
- [ ] Test edge cases extensively

## Technical Considerations

### Network Protocol
- Zone transition messages need sequence numbers
- Acknowledge zone changes to prevent race conditions
- Handle disconnections during transitions gracefully

### Client Prediction
- Client should predict zone changes for responsiveness
- Server validates and corrects if needed
- Smooth correction without jarring snaps

### Performance
- Monitor zone transition frequency
- Track connection establishment time
- Measure latency impact
- Profile memory usage with multiple connections

### Security
- Validate all position updates server-side
- Prevent teleportation exploits
- Rate-limit zone transitions
- Log suspicious transition patterns

## Metrics to Track

1. **Zone Transition Frequency**
   - Transitions per player per minute
   - Oscillation detection (rapid back-forth)

2. **Connection Performance**
   - Connection establishment time
   - Connection reuse rate
   - Failed connection attempts

3. **User Experience**
   - Lag spikes during transitions
   - Visual glitches reported
   - Player feedback on boundary behavior

4. **Server Load**
   - CPU usage during transitions
   - Memory usage for connections
   - Network bandwidth per transition

## Configuration Parameters

```yaml
zoneTransitions:
  oneWayHysteresis:
    enabled: true
    reentryThreshold: 30  # units into previous zone required
    bouncyWall:
      enabled: true
      elasticity: 0.8  # velocity reflection coefficient
    visual:
      borderColor: "#FF4444"
      chevronAnimation: true
      fadeTime: 2.0  # seconds
  
  future:
    boundaryOverlap:
      enabled: false
      overlapWidth: 50  # units
      primaryZoneThreshold: 0.6  # 60% into zone to be primary
    
    predictiveConnections:
      enabled: false
      maxAdjacentConnections: 8
      keepAliveInterval: 5000  # ms
```

## Testing Scenarios

1. **Boundary Hover Test**
   - Move back and forth across boundary
   - Verify no oscillation occurs
   - Check bouncy wall physics

2. **Diagonal Movement**
   - Move diagonally across zone corners
   - Verify smooth transitions
   - No double transitions

3. **High-Speed Crossing**
   - Sprint across boundaries
   - Verify transitions register correctly
   - No skipped zones

4. **Combat at Boundaries**
   - Fight enemies near boundaries
   - Projectiles cross zones correctly
   - No exploit opportunities

5. **Network Failure**
   - Disconnect during transition
   - Verify graceful recovery
   - Player position consistency

## References

- [Zone Oscillation Issue Analysis](#) - Original bug report
- [GridSquare Implementation](../Shooter.Shared/Models/WorldModels.cs)
- [RPC Connection Management](../../src/Rpc/Orleans.Rpc.Client/RpcConnectionManager.cs)
- [Game Physics Engine](../Shooter.Shared/Physics/)