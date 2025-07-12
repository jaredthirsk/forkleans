# Multi-Silo Chat Testing Guide

This guide explains how to test the UFX SignalR backplane functionality with multiple Orleans silos in the Shooter sample.

## Overview

The Shooter sample now supports running multiple Orleans silos in a cluster, with chat messages distributed across all silos using the UFX SignalR backplane.

## Architecture

- **2 Orleans Silos** form a cluster
- **UFX SignalR Backplane** distributes SignalR messages across silos
- **Action Servers** are distributed across silos using round-robin
- **Clients** can connect to any silo and receive all chat messages

## Running with Aspire

The AppHost is configured to start 2 silos automatically:

```bash
cd Shooter.AppHost
dotnet run
```

This will start:
- `shooter-silo-0` (Primary) - Port 11111, Gateway 30000
- `shooter-silo-1` (Secondary) - Port 11112, Gateway 30001
- 4 Action Servers distributed across both silos
- 1 Blazor Client
- 3 Bot instances

## Testing Chat Across Silos

1. **Start the Application**:
   ```bash
   cd Shooter.AppHost
   dotnet run
   ```

2. **Open Multiple Browser Windows**:
   - Connect to the Blazor client URL shown in the Aspire dashboard
   - Open 2-3 browser windows/tabs

3. **Join the Game**:
   - Enter different player names in each window
   - Join the game

4. **Test Chat**:
   - Send a chat message from one window
   - Verify the message appears in ALL windows
   - This confirms SignalR backplane is working across silos

## How It Works

1. **Client sends chat** → SignalR Hub on Silo A
2. **Silo A broadcasts** → All local SignalR clients
3. **UFX Backplane** → Distributes message to Silo B
4. **Silo B broadcasts** → All its local SignalR clients
5. **Result**: All clients across both silos receive the message

## Monitoring

In the Aspire dashboard, you can see:
- Both silos running
- Action servers distributed across silos
- SignalR connections on each silo

## Troubleshooting

1. **Chat not working across silos**:
   - Check both silos are running and healthy
   - Verify UFX.Orleans.SignalRBackplane package is installed
   - Check logs for SignalR backplane initialization

2. **Silos not forming cluster**:
   - Verify primary silo starts first
   - Check clustering configuration in logs
   - Ensure ports 11111-11112 and 30000-30001 are available

3. **Connection issues**:
   - Each silo has its own HTTPS endpoint
   - Clients can connect to any silo's gateway
   - Orleans handles routing between silos

## Technical Details

- **Clustering**: Uses `UseLocalhostClustering` with primary silo configuration
- **Storage**: `UFX.Orleans.SignalRBackplane.Constants.StorageName` grain storage
- **SignalR**: `.AddSignalRBackplane()` enables cross-silo message distribution

## Manual Testing Script

A manual test script is also available:
```bash
./test-multi-silo-chat.ps1
```

This script starts 2 silos manually for testing outside of Aspire.