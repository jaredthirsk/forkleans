# ActionServer Phaser View Guide

## Overview

The ActionServer now includes an optional read-only Phaser view that provides real-time visualization of what the server sees. This helps developers understand server state, entity positioning, and cross-zone interactions.

## Enabling the Phaser View

### Command-line Option

Start the ActionServer with the `--phaser-view` or `--phaser` flag:

```bash
dotnet run -- --phaser-view
# or
dotnet run -- --phaser --transport=litenetlib
```

### With Aspire

When using Aspire orchestration, modify the AppHost to pass the flag:

```csharp
// In Shooter.AppHost/Program.cs
var actionServer = builder.AddProject<Projects.Shooter_ActionServer>("actionserver", "--phaser-view");
```

## Accessing the View

When the Phaser view is enabled:
1. Navigate to the ActionServer's HTTP endpoint (e.g., `http://localhost:7072`)
2. You'll be automatically redirected to `/phaser`
3. The view will connect via SignalR for real-time updates

## Features

### View Modes

1. **Local Entities** - Shows entities managed by this ActionServer
   - Players in the zone
   - Enemies, factories, asteroids
   - Projectiles
   
2. **Adjacent Zones** - Shows entities from neighboring ActionServers
   - Toggle to enable/disable
   - Select specific zones to monitor
   - Entities shown in different colors

### Visual Indicators

- **Colors**:
  - Green: Local players
  - Light Green: Adjacent zone players
  - Red: Enemies
  - Orange: Factories
  - Gray: Asteroids
  - Yellow: Projectiles
  
- **Zone Boundaries**:
  - Purple border: Current zone
  - Blue border: Selected adjacent zones
  - Grid lines: 100-unit divisions

### Statistics Panel

Real-time statistics showing:
- Local entity count
- Adjacent entity count
- Player count across zones
- Enemy count
- Factory count
- Update rate (updates per second)

### Connection Status

- Green dot: Connected to server
- Red dot: Disconnected
- Auto-reconnection on connection loss

## Use Cases

1. **Debugging Zone Transitions**
   - Watch players move between zones
   - Verify handoff between ActionServers
   - Check zone boundary behavior

2. **Performance Monitoring**
   - Monitor entity counts
   - Check update rates
   - Observe server load distribution

3. **Game Balance Testing**
   - Observe enemy spawn patterns
   - Monitor factory placements
   - Track player distribution

4. **Cross-Zone Interactions**
   - Verify entity visibility across zones
   - Test projectile behavior at boundaries
   - Monitor adjacent zone connectivity

## Technical Details

### Architecture

- **SignalR Hub**: `WorldStateHub` provides real-time updates
- **Update Rate**: 10 FPS (100ms intervals)
- **Data Flow**: WorldSimulation → SignalR → Phaser.js
- **Rendering**: Phaser.js 3.70.0 with canvas renderer

### Endpoints

- `/phaser` - Main view page
- `/worldStateHub` - SignalR connection
- `/api/phaser/config` - Server configuration
- `/api/phaser/adjacent-zones` - Available zones list

### Performance Considerations

- Lightweight: Read-only, no game logic
- Efficient updates: Only sends changed data
- Optional: No overhead when disabled
- Scalable: Each server has independent view

## Troubleshooting

### View Not Loading

1. Ensure `--phaser-view` flag is provided
2. Check browser console for errors
3. Verify SignalR connection in network tab

### No Updates Received

1. Check connection status indicator
2. Verify WorldSimulation is running
3. Check server logs for errors

### Adjacent Zones Not Showing

1. Click "Adjacent Zones" toggle
2. Select specific zones from list
3. Ensure other ActionServers are running

## Example Usage

### Single Server

```bash
# Terminal 1: Start Silo
cd Shooter.Silo
dotnet run

# Terminal 2: Start ActionServer with Phaser view
cd Shooter.ActionServer
dotnet run -- --phaser-view

# Open browser to http://localhost:7072
```

### Multiple Servers

```bash
# Start multiple ActionServers with different ports
RPC_PORT=12001 HTTP_PORT=7072 dotnet run -- --phaser-view
RPC_PORT=12002 HTTP_PORT=7073 dotnet run -- --phaser-view
RPC_PORT=12003 HTTP_PORT=7074 dotnet run -- --phaser-view

# Open each in a browser tab to see different zones
```

### With Test Mode

```bash
# Run with Aspire and Phaser views
cd Shooter.AppHost
dotnet run

# Modify AppHost to add --phaser-view to ActionServer args
```

## Future Enhancements

Potential improvements for the Phaser view:
- Zoom controls
- Entity filtering options
- Performance graphs
- Event log panel
- Pause/resume functionality
- Entity detail inspection
- Network traffic visualization