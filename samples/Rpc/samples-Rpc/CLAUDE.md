# CLAUDE.md - Shooter RPC Sample

This file provides guidance to Claude Code when working with the Shooter RPC sample in this directory.

## Overview

This is a distributed multiplayer space shooter game demonstrating Granville RPC capabilities. It showcases:
- **Granville RPC** over UDP for real-time game communication
- **Orleans** for distributed game world management
- **Blazor** for web-based game client
- **.NET Aspire** for orchestration

## Architecture

### Components
1. **Shooter.Silo** - Orleans host managing world state and player grains
2. **Shooter.ActionServer** - Game logic servers using Granville RPC
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

### Automated Tests
The Shooter.Tests project contains unit and integration tests. From the `test` directory:
```bash
cd test

# Run simple unit tests (recommended)
./run-tests.sh --simple

# Run all tests (integration tests may timeout)
./run-tests.sh

# See all options
./run-tests.sh --help
```

### Manual Integration Testing
When xUnit tests timeout, use manual testing:
```bash
cd test

# Start services for manual testing
./manual-integration-test.sh --with-bot

# Monitor logs in another terminal
tail -f ../logs/*.log
```

### Test Documentation
- See `test/TEST_SCRIPTS_README.md` for detailed test script usage
- See `test/Shooter.Tests/TESTING_GUIDE.md` for test architecture details

## Useful Scripts

### Process Management
Two helper scripts are provided for managing Shooter processes:

- **`scripts/kill-shooter-processes.sh`** - Kills all running Shooter processes
  ```bash
  scripts/kill-shooter-processes.sh
  ```

- **`scripts/show-shooter-processes.sh`** - Shows all running Shooter processes with details
  ```bash
  scripts/show-shooter-processes.sh
  ```
  Shows PID, component name, working directory, and memory usage

- **`scripts/trust-dev-cert.sh`** - Trusts the development certificate for HTTPS
  ```bash
  scripts/trust-dev-cert.sh
  ```

### Debugging Workflow
1. Check running processes:
   ```bash
   scripts/show-shooter-processes.sh
   ```

2. Kill stale processes before starting fresh:
   ```bash
   scripts/kill-shooter-processes.sh
   ```

3. Monitor logs during execution:
   ```bash
   # Silo logs
   tail -f Shooter.Silo/logs/*.log

   # ActionServer logs
   tail -f Shooter.ActionServer/logs/*.log

   # Bot logs
   tail -f Shooter.Bot/logs/*.log
   ```

## Troubleshooting
- **Port conflicts**: Check if ports 7071-7073, 5000 are available
- **Orleans connection**: Ensure Silo starts before ActionServers
- **NuGet packages**: Verify local NuGet feed is configured for Granville packages
- **Browser compatibility**: Test with modern browsers supporting Canvas API
- **Stale processes**: Use `scripts/kill-shooter-processes.sh` to clean up
- **Process inspection**: Use `scripts/show-shooter-processes.sh` to see what's running
- logs should go in `./logs/`, though some are currently in other folders such as `Shooter.Silo/logs/*.log`

## Documentation

All documentation is organized in the `docs/` directory:
- Active documentation and TODO lists are in `docs/`
- Historical/completed documentation is in `docs/historical/`
- See `docs/INDEX.md` for a complete list of available documentation
