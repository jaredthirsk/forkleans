(Claude Code is to write info about tasks it finisheshere, following instructions in TODO.md.)

## Completed Tasks (2025-06-20)

### Factory Entity Type Implementation
- Added Factory entity type to EntityType enum in WorldModels.cs
- Implemented factory spawning (0-3 factories per zone) in WorldSimulation.cs
- Factories have 500 HP and are immobile buildings
- Added visual rendering for factories in both Canvas and Phaser views:
  - Canvas: Square building with gear symbol
  - Phaser: Larger sprite with gear texture
- Factories are already damageable by bullets due to existing collision logic

### Enemy Spawning from Factories
- Modified SpawnEnemies method to spawn enemies only near factories
- Enemies spawn within 40 units of a factory (at least 20 units away)
- If no factories exist in a zone, no enemies can spawn
- Position is clamped to stay within zone boundaries

### Scout HP Reduction
- Reduced Scout enemy HP from 300 to 200 as requested

### Minimap UI (Partial Implementation)
- Created Minimap.razor component to display zone statistics
- Shows factory count (large brown number) and enemy count (smaller red number) per zone
- Added to Game.razor sidebar
- Created API endpoint /api/world/zone-stats in WorldController
- NOTE: Zone stats currently return placeholder data (0 counts) because we don't have a mechanism to query entity counts from ActionServers via RPC

### Future Considerations

1. **Zone Stats Implementation**: To properly implement zone statistics, we need to:
   - Add a method to IGameRpcGrain to return entity counts
   - Have the WorldController query each ActionServer for its entity counts
   - Cache results to avoid excessive RPC calls

2. **Factory Destruction**: When factories are destroyed, consider:
   - Should enemies stop spawning permanently?
   - Should there be a way to rebuild factories?
   - Visual feedback for damaged factories

3. **Cross-Zone Factory Spawning**: The TODO mentioned enemies spawning from factories in other zones if local factories are destroyed. This would require:
   - Cross-zone communication for factory availability
   - Enemy transfer mechanism between zones
   - Path-finding for enemies to reach target zone

4. **Performance**: The minimap polls for stats every second. Consider:
   - WebSocket or SignalR for real-time updates
   - Batching updates to reduce server load
   - Only updating when zone data actually changes

### Scout Improvements

1. **Fixed Scout Alerting Mechanism**: 
   - Added logic to make alerted enemies actually move toward the last known player position
   - Enemies now move at 30 units/second when alerted
   - Alert expires after 30 seconds or when enemy reaches the target position
   - Alerted enemies override their normal AI behavior

2. **Improved Scout Wifi Indicator Animation**:
   - Progressive animation showing 1, 2, then 3 bars over 3 seconds
   - Gray color while building up alert (not ready)
   - Green color with flashing when ready to alert (after 3 seconds)
   - Implemented in both Canvas and Phaser views
   - Visual feedback matches the TODO specification exactly
