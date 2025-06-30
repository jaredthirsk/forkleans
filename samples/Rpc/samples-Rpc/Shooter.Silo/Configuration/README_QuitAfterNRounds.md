# QuitAfterNRounds Feature

This feature allows the Shooter.Silo to automatically shut down after a specified number of game rounds, which is useful for integration testing.

## Configuration

The feature is configured via the `GameSettings` section in appsettings.json:

```json
{
  "GameSettings": {
    "QuitAfterNRounds": 2
  }
}
```

- `QuitAfterNRounds`: Number of rounds after which the silo will shut down
  - Default: 0 (never quit)
  - For integration tests: Set to 2

## How It Works

1. The `WorldManagerGrain` tracks the number of completed rounds in its persistent state
2. When a game round ends (all enemies defeated), the ActionServer calls `NotifyGameOver()` on the WorldManagerGrain
3. The WorldManagerGrain increments the round counter and checks if it has reached the configured limit
4. If the limit is reached, it broadcasts a shutdown message and triggers graceful application shutdown

## Configuration Files

- `appsettings.json`: Default configuration (QuitAfterNRounds = 0)
- `appsettings.Development.json`: Development configuration (QuitAfterNRounds = 0)
- `appsettings.IntegrationTest.json`: Integration test configuration (QuitAfterNRounds = 2)

## Usage in Integration Tests

Set the environment to "IntegrationTest" when running tests:

```bash
ASPNETCORE_ENVIRONMENT=IntegrationTest dotnet run
```

This will load the `appsettings.IntegrationTest.json` configuration and shut down after 2 rounds.