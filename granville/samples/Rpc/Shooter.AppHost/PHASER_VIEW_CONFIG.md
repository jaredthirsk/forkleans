# Configuring Phaser View in Aspire AppHost

## Current Configuration

The Phaser view is currently **ENABLED** for all ActionServers in the AppHost.

## How to Control Phaser View

### Option 1: Enable for All (Current)
The AppHost currently enables Phaser view for all ActionServers:
```csharp
.WithArgs($"--transport={transportType} --phaser-view")
```

### Option 2: Disable Phaser View
To disable, remove the `--phaser-view` flag:
```csharp
.WithArgs($"--transport={transportType}")
```

### Option 3: Make it Configurable
Add a configuration variable at the top of Program.cs:
```csharp
// Configuration
const bool EnablePhaserView = true; // Toggle this
const int InitialActionServerCount = 4;
// ... other config ...

// Then in the ActionServer configuration:
.WithArgs($"--transport={transportType}{(EnablePhaserView ? " --phaser-view" : "")}")
```

### Option 4: Enable for Specific Servers Only
Enable Phaser view only for certain ActionServer instances:
```csharp
// Enable only for the first ActionServer (useful for debugging)
var args = $"--transport={transportType}";
if (i == 0) // Only first server
{
    args += " --phaser-view";
}
server.WithArgs(args)
```

### Option 5: Via Command Line
Make it controllable when starting the AppHost:
```csharp
// At the top, parse args
var enablePhaserView = args.Any(arg => arg == "--phaser-view");

// Then use it:
.WithArgs($"--transport={transportType}{(enablePhaserView ? " --phaser-view" : "")}")
```

Then run:
```bash
# With Phaser view
dotnet run -- --phaser-view

# Without Phaser view
dotnet run
```

## Accessing the Phaser Views

When enabled, each ActionServer will have its own Phaser view. In the Aspire dashboard:

1. Look for the ActionServer instances (shooter-actionserver-0, shooter-actionserver-1, etc.)
2. Click on the endpoint URL for each server
3. You'll be redirected to the `/phaser` view automatically

## Example URLs

After running the AppHost, the ActionServers will typically be available at:
- http://localhost:[dynamic-port]/phaser (for each ActionServer)

The exact ports are dynamically assigned by Aspire and visible in the dashboard.

## Tips

1. **Performance**: The Phaser view has minimal overhead, but if running many ActionServers, you might want to enable it only for debugging
2. **Multiple Monitors**: Open different ActionServer Phaser views in separate browser windows to monitor multiple zones simultaneously
3. **Adjacent Zones**: Use the toggle to see entities from neighboring zones - useful for debugging zone transitions