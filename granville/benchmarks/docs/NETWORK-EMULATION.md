# Network Emulation Framework

This document describes the comprehensive network condition testing framework implemented in Granville Benchmarks.

## Overview

The NetworkEmulator provides both system-level and application-level network condition simulation to test transport performance under realistic network conditions. This allows testing across a spectrum from perfect LAN to satellite connections.

## Platform Support

### Linux (tc)
- Uses Linux Traffic Control (`tc`) command for kernel-level packet manipulation
- Requires `sudo` access to modify network interfaces
- Supports latency, jitter, packet loss, and bandwidth limiting
- Most accurate emulation as it affects all network traffic

```bash
# Example tc command generated:
sudo tc qdisc add dev lo root netem delay 50ms 10ms loss 1% rate 10mbit
```

### Windows (clumsy)
- Uses clumsy.exe for Windows network emulation
- Download from: https://jagt.github.io/clumsy/
- Supports UDP traffic filtering, latency, packet loss, and throttling
- Requires administrator privileges

```bash
# Example clumsy command generated:
clumsy.exe -f "udp" --lag on --lag-time 50 --drop on --drop-chance 1.0 --throttle on --throttle-bandwidth 10000
```

### Cross-platform Fallback
- Application-level simulation when system tools unavailable
- Implemented in NetworkEmulator class methods:
  - `ShouldDropPacket()` - Random packet loss simulation
  - `GetSimulatedLatencyMs()` - Latency with jitter calculation
  - `IsWithinBandwidthLimit()` - Bandwidth throttling check

## Network Profiles

Ten predefined network condition profiles from perfect to satellite:

| Profile | Latency | Jitter | Loss | Bandwidth | Use Case |
|---------|---------|--------|------|-----------|----------|
| Perfect | 0ms | 0ms | 0% | Unlimited | Baseline testing |
| LAN | 1ms | 0ms | 0% | 1 Gbps | Local network |
| WiFi | 5ms | 2ms | 0.1% | 100 Mbps | Home/office WiFi |
| Regional | 30ms | 5ms | 0.1% | 100 Mbps | Same region servers |
| Cross-Country | 80ms | 10ms | 0.5% | 50 Mbps | Continental distance |
| International | 150ms | 20ms | 1% | 25 Mbps | Intercontinental |
| Mobile 4G | 50ms | 15ms | 2% | 10 Mbps | Mobile connection |
| Mobile 3G | 120ms | 30ms | 5% | 2 Mbps | Slower mobile |
| Congested | 200ms | 50ms | 10% | 1 Mbps | Network congestion |
| Satellite | 600ms | 100ms | 3% | 5 Mbps | Satellite internet |

## Usage Examples

### System-Level Emulation (Recommended)

```csharp
var emulator = new NetworkEmulator(logger, useSystemTools: true, networkInterface: "eth0");

// Apply regional network conditions
await emulator.ApplyConditionsAsync(NetworkProfiles.Regional);

// Run benchmarks...

// Clear conditions
await emulator.ClearConditionsAsync();
```

### Application-Level Emulation

```csharp
var emulator = new NetworkEmulator(logger, useSystemTools: false);
await emulator.ApplyConditionsAsync(NetworkProfiles.Mobile4G);

// Check in transport implementation:
if (emulator.ShouldDropPacket())
{
    // Skip sending this packet
    return;
}

var delay = emulator.GetSimulatedLatencyMs();
if (delay > 0)
{
    await Task.Delay(delay);
}
```

### Custom Network Conditions

```csharp
var customCondition = new NetworkCondition
{
    Name = "custom-poor",
    LatencyMs = 250,
    JitterMs = 100,
    PacketLoss = 0.15, // 15%
    Bandwidth = 512_000 // 512 kbps
};

await emulator.ApplyConditionsAsync(customCondition);
```

## Configuration Integration

Network conditions can be specified in benchmark configuration files:

```json
{
  "networkCondition": "international",
  "workloads": [
    {
      "type": "FpsGame",
      "playerCount": 64,
      "duration": "00:05:00"
    }
  ],
  "transports": ["LiteNetLib", "Ruffles"],
  "useSystemEmulation": true
}
```

## Testing Scripts

### Network Condition Test Script
```bash
pwsh ./scripts/test-network-conditions.ps1
```

This script:
1. Tests all 10 network profiles
2. Runs RPC latency benchmarks for each condition
3. Compares LiteNetLib vs Ruffles performance
4. Generates comparative analysis reports

### MMO Scaling Test Script
```bash
pwsh ./scripts/test-mmo-scaling.ps1
```

Tests MMO workload under various network conditions with scaling player counts.

## Performance Impact Analysis

The framework measures how network conditions affect:

1. **RPC Latency**: Round-trip time for method calls
2. **Throughput**: Messages per second capacity
3. **Error Recovery**: How transports handle packet loss
4. **Jitter Tolerance**: Consistency under variable latency
5. **Bandwidth Efficiency**: Performance under bandwidth constraints

## Best Practices

### For Development Testing
- Use application-level emulation for quick testing
- Start with WiFi profile for realistic baseline
- Test Regional and International for distributed scenarios

### For Performance Validation
- Use system-level emulation for accurate results
- Test all profiles to find performance cliffs
- Focus on Mobile profiles for mobile game development
- Include Congested profile for stress testing

### For CI/CD Integration
- Use application-level emulation to avoid privilege requirements
- Include subset of profiles (Perfect, WiFi, Regional, Mobile4G)
- Set performance thresholds based on target network conditions

## Limitations

### System-Level Emulation
- Requires administrative privileges
- May affect other network traffic on the machine
- Platform-specific tool dependencies

### Application-Level Emulation
- Less accurate than kernel-level manipulation
- Transport-specific implementation required
- May not capture all real-world network behaviors

## Future Enhancements

1. **Dynamic Conditions**: Support for changing conditions during test execution
2. **Real Network Replay**: Capture and replay actual network traces
3. **Multi-Path Testing**: Simulate different network paths for different clients
4. **Congestion Control**: More sophisticated bandwidth limiting algorithms
5. **Mobile Pattern Simulation**: Simulate mobile handoffs and signal strength changes