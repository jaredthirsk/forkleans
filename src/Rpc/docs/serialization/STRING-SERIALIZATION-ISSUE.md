# String Serialization Issue in RPC

## Problem
Player IDs (strings) are being deserialized as null when passed through RPC ConnectPlayer method, even though the client is sending valid GUIDs.

## Symptoms
1. Client sends: `ConnectPlayer("6a0d4e53-4eab-4a69-9bf0-d470f757249e")`
2. Server receives: `ConnectPlayer(null)`
3. Results in: `ArgumentNullException: Value cannot be null. (Parameter 'key')`

## Root Cause Analysis
The issue appears to be that:
1. The RPC system is not properly configured with serializers for application types
2. String parameters in RPC methods are being incorrectly deserialized as null
3. The ActionServer is not explicitly configuring RPC serialization

## Investigation Steps Taken
1. Added validation to prevent null/empty player IDs on client side ✓
2. Added validation on server side to catch null player IDs ✓
3. Verified that registration returns valid player IDs ✓
4. Confirmed that all ConnectPlayer calls are failing with valid GUIDs

## Solution Required
Need to ensure that:
1. RPC server is configured with proper serialization for application types
2. String parameters are correctly serialized/deserialized
3. The serializer configuration includes the Shooter.Shared assembly types

## Temporary Workaround
The defensive null checks we added will prevent crashes, but the root serialization issue needs to be fixed for the game to work properly.