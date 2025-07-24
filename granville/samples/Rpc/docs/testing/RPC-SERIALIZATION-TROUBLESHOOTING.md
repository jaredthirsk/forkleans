# RPC Serialization Troubleshooting Guide

## Issue: Bot Connection Failure with Serialization Errors

### Problem Description
When the bot attempts to connect to the game server via RPC, it encounters serialization errors that prevent successful connection.

### Error Manifestations

#### 1. UnsupportedWireTypeException
```
Orleans.Serialization.UnsupportedWireTypeException: A WireType value of LengthPrefixed is expected by this codec. [VarInt, IdDelta:0, SchemaType:Expected]
```

**Location**: Client-side deserialization in `OutsideRpcRuntimeClient`

**Cause**: The client expects a different wire type format than what the server is sending.

#### 2. Null PlayerId on Server
```
RPC: ConnectPlayer called with null or empty playerId
```

**Location**: Server-side in `GameRpcGrain.ConnectPlayer`

**Cause**: The playerId argument is not being properly serialized/deserialized during RPC transmission.

### Debugging Steps

1. **Check Client Logs** (`logs/bot-0.log`)
   - Look for serialization exceptions
   - Verify the playerId is set before RPC call
   - Check if RPC handshake completes successfully

2. **Check Server Logs** (`logs/actionserver-*.log`)
   - Look for deserialization of arguments
   - Check raw argument bytes
   - Verify method mapping

3. **Key Log Entries to Look For**

   Client side:
   ```
   Connecting player {PlayerId} via RPC
   Failed to connect to game server
   ```

   Server side:
   ```
   [RPC_SERVER] Raw argument bytes (7): 00200003C101E0
   [RPC_SERVER] Deserialized argument[0]: Type=null, Value=null
   ```

### Root Cause Analysis

The issue appears to be a two-part problem:

1. **Argument Serialization**: The client is not properly serializing the string playerId argument
2. **Response Deserialization**: The client fails to deserialize the server's response due to wire type mismatch

### Temporary Workarounds

None available - this is a fundamental serialization incompatibility that requires code fixes.

### Permanent Solutions

1. **Fix RPC Argument Serialization**
   - Ensure `RpcSerializationSessionFactory.SerializeArgumentsWithIsolatedSession` properly handles string arguments
   - Verify marker bytes are correctly applied

2. **Fix Response Deserialization**
   - Update `OutsideRpcRuntimeClient.DeserializePayload` to handle marker bytes correctly
   - Ensure wire type expectations match between client and server

### Related Files

- `/src/Rpc/Orleans.Rpc.Client/OutsideRpcRuntimeClient.cs`
- `/src/Rpc/Orleans.Rpc.Client/RpcSerializationSessionFactory.cs`
- `/src/Rpc/Orleans.Rpc.Server/RpcConnection.cs`
- `/granville/samples/Rpc/Shooter.ActionServer/Grains/GameRpcGrain.cs`

### Testing After Fix

1. Build updated Granville RPC packages with bumped revision
   ```bash
   cd /mnt/c/forks/orleans
   pwsh granville/scripts/build-all-granville.ps1 -BumpRevision -SkipShims -SkipSample
   ```
   
   Or to build only RPC packages:
   ```bash
   pwsh granville/scripts/bump-granville-version.ps1  # Bump revision first
   pwsh granville/scripts/build-granville-rpc-packages.ps1 -Configuration Release
   ```

2. Rebuild Shooter projects to use updated NuGet packages
   ```bash
   cd granville/samples/Rpc
   dotnet restore --force  # Force restore to get new package versions
   dotnet build -c Debug
   ```

3. Run AppHost with `./rl.sh`  (This will kill existing processes.  **DO NOT** run via dotnet-win.  You can also just kill with `./k.sh`)
   ```bash
   cd Shooter.AppHost
   ./rl.sh
   ```

4. Monitor bot connection in logs
   ```bash
   tail -f ../logs/bot-0.log
   ```

5. Verify:
   - No serialization exceptions
   - PlayerId is received correctly on server
   - ConnectPlayer returns "SUCCESS"
   - Bot proceeds to game loop

### Prevention

1. Add unit tests for RPC serialization with various argument types
2. Add integration tests for client-server RPC communication
3. Consider adding serialization format versioning
4. Implement better error messages for serialization mismatches

## Known Issues

### 1. Orleans Proxy IInvokable Initialization
Orleans-generated proxies don't properly initialize IInvokable objects with arguments in the RPC context. The proxy creates empty argument arrays instead of capturing actual method arguments.

**Current Workaround**: Created `RpcProxyProvider` that always returns false to force use of RpcGrainReference instead of Orleans proxies.

### 2. Namespace Conflict with Orleans.GrainReferences.RpcProvider
Orleans Core has its own `RpcProvider` class in the `Orleans.GrainReferences` namespace that conflicts with RPC's implementation.

**Fix**: Renamed our class to `RpcProxyProvider` in the `Granville.Rpc` namespace to avoid the conflict.