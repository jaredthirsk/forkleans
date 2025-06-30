# Bullet Zone Transfer Fix

## Problem
Bullets were only accepted by neighboring zones after they had already crossed the boundary, causing:
- Visual gaps at zone edges
- Bullets appearing suddenly rather than smoothly transitioning
- Poor gameplay experience near zone boundaries

## Solution Implemented

Modified `ReceiveBulletTrajectory` to accept bullets in three scenarios:

1. **Currently in zone**: The bullet is already within our zone boundaries
2. **Will enter soon**: The bullet will enter our zone within the next 0.5 seconds
3. **Recently left**: The bullet originated from our zone within the last 0.2 seconds

### Key Changes

```csharp
// Look ahead to see if bullet will enter our zone
const float lookAheadTime = 0.5f; // Look ahead half a second
const float sampleInterval = 0.05f; // Sample every 50ms

// Check future positions along trajectory
for (float t = 0; t <= Math.Min(lookAheadTime, remainingLifespan); t += sampleInterval)
{
    var futurePosition = origin + velocity * (elapsedTime + t);
    if (GridSquare.FromPosition(futurePosition) == _assignedSquare)
    {
        // Accept this bullet - it will enter our zone soon
        break;
    }
}
```

## Benefits

1. **Smooth Transitions**: Bullets are visible before they cross zone boundaries
2. **Better Combat**: Players can react to incoming bullets from adjacent zones
3. **No Visual Gaps**: Continuous bullet trajectories across zones
4. **Maintains Performance**: Only looks ahead 0.5 seconds with 50ms sampling

## Technical Details

- **Look-ahead time**: 0.5 seconds (configurable)
- **Sample interval**: 50ms (10 samples maximum)
- **Origin grace period**: 200ms for bullets leaving the zone
- **Debug logging**: Tracks why bullets are accepted/rejected

## Impact on Warnings

The "entities outside assigned zone" warnings are now expected and correct:
- Bullets will legitimately exist outside their origin zone
- This enables smooth visual transitions
- Warnings remain at Debug level for diagnostic purposes