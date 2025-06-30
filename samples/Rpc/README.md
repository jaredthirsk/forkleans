# Forkleans Space Shooter Demo

This demo showcases a distributed multiplayer space shooter game using:
- **Forkleans RPC** for UDP-based real-time communication
- **Microsoft Orleans** for distributed game world management
- **Blazor Desktop** for the game client
- **.NET Aspire** for orchestrating the distributed system

## Architecture

### Components

1. **Shooter.Silo** - Orleans cluster (single node) with Web API
   - Manages world grid assignments
   - Tracks player state
   - Coordinates action servers

2. **Shooter.ActionServer** - Forkleans RPC server + Orleans client
   - Runs physics simulation for assigned grid squares
   - Handles real-time player input via UDP
   - Manages enemies and combat

3. **Shooter.Client** - Blazor Desktop application
   - Canvas-based game rendering
   - Keyboard input handling
   - Connects to action servers via Forkleans RPC

4. **Shooter.Shared** - Common models and interfaces

## Prerequisites

- .NET 8.0 SDK
- Forkleans NuGet packages (configured in local feed)
- .NET Aspire workload: `dotnet workload install aspire`

## Running the Demo

### Option 1: Using .NET Aspire (Recommended)

1. Run the Aspire AppHost:
   ```bash
   dotnet run --project Shooter.AppHost
   ```

2. Open the Aspire dashboard (URL shown in console)

3. The AppHost will start:
   - 1 Orleans Silo
   - 2 ActionServer instances

4. Run the client separately (it's a desktop app):
   ```bash
   dotnet run --project Shooter.Client
   ```

### Option 2: Manual Start

1. Start the Orleans Silo:
   ```bash
   dotnet run --project Shooter.Silo
   ```

2. Start one or more ActionServers:
   ```bash
   dotnet run --project Shooter.ActionServer
   ```

3. Run the client:
   ```bash
   dotnet run --project Shooter.Client
   ```

## Gameplay

- Enter your name and click "Connect"
- Use **WASD** keys to move
- Press **Space** to shoot
- Avoid enemies and their bullets
- The game world is distributed across multiple action servers

## Configuration

### Silo URL
The ActionServers need to know where the Orleans Silo is running. This is configured via:
- Environment variable: `Orleans__SiloUrl` (handled automatically by Aspire)
- appsettings.json: `Orleans:SiloUrl` (default: https://localhost:7071)

### Local NuGet Feed
Ensure your NuGet.config includes the local Forkleans feed:
```xml
<packageSources>
  <add key="LocalForkleans" value="../../local-packages" />
</packageSources>
```

## Architecture Notes

### Distributed World Management
- The game world is divided into grid squares
- Each ActionServer is assigned one or more grid squares
- Players are automatically assigned to the correct ActionServer based on position

### Networking
- Orleans Silo uses HTTP/gRPC for cluster communication
- ActionServers use Forkleans RPC over UDP for real-time game data
- Client discovers ActionServers through the Silo's REST API

### Reusable Components
The Blazor components (GameCanvas, GameControls) are designed to be reusable for:
- Web-based spectator views
- Different UI frameworks
- Future game projects

## Troubleshooting

1. **Build errors**: Ensure all Orleans packages have the `Microsoft.` prefix
2. **Connection issues**: Check firewall settings for UDP traffic
3. **Aspire issues**: Ensure the Aspire workload is installed
4. **Package not found**: Verify local NuGet feed configuration