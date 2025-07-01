# Port Allocation Strategy for Multiple ActionServers

## Overview
When running multiple ActionServer instances, we need to ensure each gets unique ports for both HTTP and RPC endpoints.

## Solution

### 1. HTTP Ports
- Let Aspire handle HTTP port allocation dynamically
- Use `.WithHttpsEndpoint()` in AppHost to let Aspire assign unique ports
- ActionServers will run on different HTTP ports automatically

### 2. RPC Ports (Forkleans)
We use a three-tier strategy:

#### Tier 1: Environment Variable (Highest Priority)
```csharp
var envPort = Environment.GetEnvironmentVariable("RPC_PORT");
```
- If `RPC_PORT` is set, use that specific port
- Used by Aspire orchestration to assign specific ports

#### Tier 2: Instance ID Based (Aspire)
```csharp
var instanceId = Environment.GetEnvironmentVariable("ASPIRE_INSTANCE_ID");
var rpcPort = 12000 + instanceId;
```
- Use instance ID to calculate unique port offset
- Ensures deterministic port assignment

#### Tier 3: Dynamic Discovery (Fallback)
```csharp
// Find available port with random delay to avoid races
await Task.Delay(random.Next(100, 500));
for (int port = 12000; port < 13000; port++)
{
    // Try to bind and release
}
```
- Random delay reduces race conditions
- Scans port range for availability

## Port Ranges

| Service | Port Range | Protocol | Notes |
|---------|------------|----------|-------|
| Silo HTTP | 7071 | HTTPS | Fixed |
| Silo Orleans | 11111, 30000 | TCP | Fixed |
| ActionServer HTTP | Dynamic | HTTPS | Aspire assigns |
| ActionServer RPC | 12000-12008 | UDP | Based on instance ID |
| Client | 5000 | HTTP | Fixed |

## Aspire Configuration

```csharp
// Create 9 ActionServer instances with unique RPC ports
for (int i = 0; i < 9; i++)
{
    var rpcPort = 12000 + i;
    builder.AddProject<Projects.Shooter_ActionServer>($"shooter-actionserver-{i}")
        .WithEnvironment("RPC_PORT", rpcPort.ToString())
        .WithEnvironment("ASPIRE_INSTANCE_ID", i.ToString())
        .WithHttpsEndpoint(); // Dynamic HTTP port
}
```

## Benefits
1. **No Port Conflicts**: Each instance gets unique ports
2. **Deterministic**: Instance 0 always gets RPC port 12000, instance 1 gets 12001, etc.
3. **Scalable**: Easy to add more instances
4. **Debuggable**: Can identify instances by their ports

## Troubleshooting
- Check Aspire dashboard for port assignments
- Look for "Using RPC port" messages in logs
- Verify no other processes are using ports 12000-12999
- Use `netstat -an | grep 12000` to check port usage