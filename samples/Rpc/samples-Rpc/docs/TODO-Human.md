TODO-Human.md

# Tasks for a human to do

## Analyze Claude's findings

 Based on my analysis of the logs, here's what's happening:

  Issues Found:

  1. Pre-established connections are failing:
    - Connections to adjacent zones are being marked as "DEAD"
    - Zone (2,2) connection shows as disconnected even though it's the player's current zone
    - The client is maintaining the connection list but they're not staying alive
  2. Invalid zone requests:
    - When the player is in zone (2,2) near the bottom border, it's trying to fetch from zones (1,3) and (2,3) which don't exist
    - The grid is 3x3 (zones 0,0 through 2,2), so there are no zones with Y=3
  3. No entities are being fetched:
    - Even when connections to valid zones like (1,2) show as "Connected", no entities are being fetched
    - The log messages for "Fetched X entities from zone" or "Added X entities from adjacent zones" never appear
  4. Connection timeouts:
    - RPC requests are timing out after 30 seconds
    - This suggests network connectivity issues between the client and adjacent zone servers

  Root Causes:

  1. Zone boundary check is incorrect: The code needs to validate that requested zones actually exist before trying to fetch from them
  2. Pre-established connections aren't working properly: Either they're not being established correctly or they're dying immediately
  3. Possible RPC configuration issue: The connections might not be configured properly for cross-zone communication

  The cross-zone visibility logic is correctly detecting when the player is near borders and identifying which zones to fetch from, but the actual fetching mechanism is failing due to connection issues
  and invalid zone requests.

## Class conflict

 Most Likely Service Conflicts

  The services most likely causing the "No active nodes compatible" error are:

  1. GrainFactory (HIGH RISK)
    - Orleans client registers its GrainFactory
    - RPC also registers RpcGrainFactory as GrainFactory
    - Since both use TryAddSingleton, whichever registers first wins
    - When RPC wins, Orleans grain lookups fail
  2. IGrainFactory/IInternalGrainFactory (HIGH RISK)
    - Both systems map these interfaces from their GrainFactory
    - If RPC's factory is registered, Orleans grains can't be created
  3. IGrainReferenceActivatorProvider Order (MEDIUM RISK)
    - RPC adds RpcGrainReferenceActivatorProvider first
    - This provider may intercept Orleans grain creation requests
    - Provider order matters for grain reference activation
  4. Manifest Provider Resolution (MEDIUM RISK)
    - Even with keyed services, components might resolve the wrong provider
    - Critical for GrainInterfaceTypeToGrainTypeResolver


How do you propose we resolve those most likely service conflicts?  One idea to me is we should create IRpcGrainFactory and avoid conflicts by avoiding overlapping GrainFactories.  I'm ok if  ││   we tackle one or two risks at a time, test, and then see if we need to do more.
