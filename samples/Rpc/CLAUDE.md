# CLAUDE.md - Shooter RPC Sample

This file provides guidance to Claude Code when working with the Shooter RPC sample in this directory.

## Overview

This is a distributed multiplayer space shooter game demonstrating Forkleans RPC capabilities. It showcases:
- **Forkleans RPC** over UDP for real-time game communication
- **Orleans** for distributed game world management
- **Blazor** for web-based game client
- **.NET Aspire** for orchestration

## Architecture

### Components
1. **Shooter.Silo** - Orleans host managing world state and player grains
2. **Shooter.ActionServer** - Game logic servers using Forkleans RPC
3. **Shooter.Client** - Blazor web client with canvas rendering
4. **Shooter.AppHost** - Aspire orchestration host
5. **Shooter.Shared** - Common models and grain interfaces
6. **Shooter.ServiceDefaults** - Shared Aspire configuration

### Key Patterns
- World divided into 1000x1000 unit grid squares
- Each ActionServer manages assigned grid squares
- Players routed to appropriate ActionServer based on position
- UDP-based RPC for real-time game updates

## Running the Sample

### Using .NET Aspire (Recommended)
```bash
cd Shooter.AppHost
dotnet run
```
This starts all components with proper orchestration.

### Manual Startup
1. Start the Silo:
   ```bash
   cd Shooter.Silo
   dotnet run
   ```

2. Start ActionServers (multiple instances):
   ```bash
   cd Shooter.ActionServer
   dotnet run --urls "http://localhost:7072"
   dotnet run --urls "http://localhost:7073"  # Second instance
   ```

3. Start the Client:
   ```bash
   cd Shooter.Client
   dotnet run
   ```

## Development Notes

### Game Controls
- **WASD** - Movement
- **Space** - Shoot
- **Mouse** - Aim (if implemented)

### Configuration
- Silo ports: HTTP 7071, Orleans 11111/30000
- ActionServer base port: 7072+
- Client port: 5000
- UDP game port: Dynamically assigned

### Current Limitations
- Client uses HTTP polling instead of UDP RPC
- Single-node Orleans cluster only
- Basic physics and enemy AI
- No persistence beyond session

### Extension Points
- Implement direct UDP client connection
- Add more game mechanics
- Enhance world simulation
- Add multiplayer features
- Implement proper game state synchronization

## Testing
Currently no automated tests. Manual testing:
1. Launch via AppHost
2. Navigate to http://localhost:5000
3. Enter a username and play
4. Verify player movement and shooting
5. Check ActionServer logs for simulation updates

## Troubleshooting
- **Port conflicts**: Check if ports 7071-7073, 5000 are available
- **Orleans connection**: Ensure Silo starts before ActionServers
- **NuGet packages**: Verify local NuGet feed is configured for Forkleans packages
- **Browser compatibility**: Test with modern browsers supporting Canvas API