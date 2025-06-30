# Log Optimization Guide

This document describes the changes made to reduce log chattiness in the Shooter sample applications.

## Overview

The Shooter sample was generating excessive log messages, particularly in the ActionServer component. This was impacting performance and making it difficult to identify important messages.

## Changes Made

### 1. ActionServer Configuration (`appsettings.json`)

Changed log levels for the following components from `Information` to `Warning`:
- `Granville.ClientOptionsLogger` - Eliminates verbose configuration dumps
- `Granville.Runtime.Messaging.NetworkingTrace` - Reduces connection establishment logs
- `Granville.Rpc.RpcServer` - Reduces RPC server lifecycle logs
- `Granville.Rpc.Transport` - Reduces transport connection logs
- `Shooter.ActionServer.Simulation.WorldSimulation` - Reduces zone boundary warnings
- `Shooter.ActionServer.Grains.GameRpcGrain` - Reduces bullet trajectory logs

### 2. Code Changes

#### WorldSimulation.cs
- Changed zone boundary warnings from `LogWarning` to `LogDebug` (line 260)
  - These were generating multiple messages per second
- Changed bullet spawn logs from `LogInformation` to `LogDebug` (line 1882)
  - These were generating logs for every bullet transfer

#### GameRpcGrain.cs
- Changed bullet trajectory receive logs from `LogInformation` to `LogDebug` (line 122)
  - These were generating logs for every bullet crossing zone boundaries

#### Program.cs
- Commented out `DiagnosticService` registration (line 252)
  - This was dumping service configuration on every startup

### 3. Silo Configuration (`Shooter.Silo/appsettings.json`)

Added log level overrides to reduce Orleans framework chattiness:
- `Granville.ClientOptionsLogger` → `Warning`
- `Granville.Runtime.NetworkingTrace` → `Warning`
- `Granville.Messaging` → `Warning`

### 4. Bot Configuration (`Shooter.Bot/appsettings.json`)

Changed default log level from `Debug` to `Information` and added overrides:
- `Default` → `Information` (was `Debug`)
- `Shooter` → `Information` (was `Debug`)
- Added framework overrides similar to ActionServer

## Results

These changes significantly reduce log volume while maintaining visibility of important events:

### Still Logged (Information Level)
- Service startup/shutdown
- Player connections/disconnections
- Zone assignments
- Error conditions
- Stats reporting

### Now Debug Level (Hidden by Default)
- Zone boundary crossings
- Bullet trajectory transfers
- Detailed world state updates
- Configuration dumps

## Monitoring Log Volume

With the new log metrics implementation, you can monitor log volume using:
- `actionserver_log_messages_total` - Total log messages by level
- `actionserver_log_rate_per_minute` - Current log rate
- Similar metrics for Silo and Bot components

## Reverting Changes

To increase log verbosity for debugging:

1. **Temporary**: Set environment variable `Logging__LogLevel__Default=Debug`
2. **Permanent**: Edit the respective `appsettings.json` files

## Best Practices

1. Use `LogDebug` for high-frequency events
2. Use `LogInformation` for important state changes
3. Use `LogWarning` for recoverable issues
4. Use `LogError` for failures requiring attention
5. Consider rate limiting for repetitive messages
6. Use structured logging with appropriate properties