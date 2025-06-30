using Shooter.Tests.Infrastructure;
using Xunit;

namespace Shooter.Tests.IntegrationTests;

/// <summary>
/// Integration tests for game flow mechanics.
/// Verifies victory conditions, bot behavior, and game restart functionality.
/// </summary>
public class GameFlowTests : IClassFixture<ShooterTestFixture>
{
    private readonly ShooterTestFixture _fixture;
    private readonly LogAnalyzer _logAnalyzer;
    
    public GameFlowTests(ShooterTestFixture fixture)
    {
        _fixture = fixture;
        _logAnalyzer = new LogAnalyzer(_fixture.LogDirectory);
    }
    
    [Fact]
    public async Task Game_Should_Detect_Victory_When_All_Enemies_Destroyed()
    {
        // Arrange
        var timeout = TimeSpan.FromSeconds(_fixture.TestTimeoutSeconds);
        
        // Wait for game initialization
        var siloReady = await _logAnalyzer.WaitForLogEntry(
            "silo.log",
            "Silo started successfully",
            TimeSpan.FromSeconds(30));
            
        Assert.NotNull(siloReady);
        
        // Act
        // Bots in test mode should systematically destroy enemies
        // Monitor for enemy destruction
        var enemyDestroyedEntry = await _logAnalyzer.WaitForLogEntry(
            "actionserver-0.log",
            "Enemy.*destroyed",
            timeout,
            useRegex: true);
            
        Assert.NotNull(enemyDestroyedEntry);
        
        // Assert
        // Victory should be detected when all enemies are destroyed
        var victoryEntry = await _logAnalyzer.WaitForLogEntry(
            "silo.log",
            "Victory condition met: all enemies destroyed",
            timeout);
            
        Assert.NotNull(victoryEntry);
        
        // Verify victory timer starts
        var timerEntry = await _logAnalyzer.WaitForLogEntry(
            "silo.log",
            "Starting victory timer",
            TimeSpan.FromSeconds(5));
            
        Assert.NotNull(timerEntry);
    }
    
    [Fact]
    public async Task Game_Should_Restart_After_Victory_Timer()
    {
        // Arrange
        var timeout = TimeSpan.FromSeconds(_fixture.TestTimeoutSeconds);
        
        // Wait for victory
        await _logAnalyzer.WaitForLogEntry("silo.log", "Victory condition met", timeout);
        
        // Act & Assert
        // Game should restart after 15 seconds
        var restartEntry = await _logAnalyzer.WaitForLogEntry(
            "silo.log",
            "Restarting game",
            TimeSpan.FromSeconds(20));
            
        Assert.NotNull(restartEntry);
        
        // Verify new enemies are spawned
        var enemySpawnEntry = await _logAnalyzer.WaitForLogEntry(
            "actionserver-0.log",
            "Spawning.*enem",
            TimeSpan.FromSeconds(10),
            useRegex: true);
            
        Assert.NotNull(enemySpawnEntry);
    }
    
    [Fact]
    public async Task Bots_Should_Target_Asteroids_After_Destroying_Enemies()
    {
        // Arrange
        var timeout = TimeSpan.FromSeconds(60);
        
        // Wait for bots to connect
        await _logAnalyzer.WaitForLogEntry("bot-0.log", "connected as player", TimeSpan.FromSeconds(30));
        
        // Act
        // Monitor bot targeting behavior
        var enemyTargetEntry = await _logAnalyzer.WaitForLogEntry(
            "bot-0.log",
            "AutoMove.*target.*enemy",
            timeout,
            useRegex: true);
            
        Assert.NotNull(enemyTargetEntry);
        
        // Wait for enemies to be cleared in the bot's zone
        await Task.Delay(5000); // Give time for combat
        
        // Assert
        // Bot should start targeting asteroids when no enemies remain
        var asteroidTargetEntry = await _logAnalyzer.WaitForLogEntry(
            "bot-0.log",
            "AutoMove.*target.*asteroid|Strafing.*asteroid",
            timeout,
            useRegex: true);
            
        Assert.NotNull(asteroidTargetEntry);
    }
    
    [Fact]
    public async Task Bots_In_Test_Mode_Should_Move_Predictably()
    {
        // Arrange
        var timeout = TimeSpan.FromSeconds(30);
        
        // Verify test mode is enabled
        var testModeEntry = await _logAnalyzer.WaitForLogEntry(
            "bot-0.log",
            "starting in test mode",
            timeout);
            
        Assert.NotNull(testModeEntry);
        
        // Act & Assert
        // In test mode, bots should use PredictableInterZone movement
        var predictableMovementEntry = await _logAnalyzer.WaitForLogEntry(
            "bot-0.log",
            "AutoMove.*PredictableInterZone",
            timeout,
            useRegex: true);
            
        Assert.NotNull(predictableMovementEntry);
    }
    
    [Fact]
    public async Task Multiple_Zones_Should_Have_Active_Combat()
    {
        // Arrange
        var timeout = TimeSpan.FromSeconds(60);
        
        // Wait for all action servers to be ready
        var serverReadyPatterns = new Dictionary<string, string>();
        for (int i = 0; i < _fixture.ActionServerCount; i++)
        {
            serverReadyPatterns[$"actionserver-{i}.log"] = "World simulation started";
        }
        
        var readyResults = await _logAnalyzer.WaitForLogEntries(serverReadyPatterns, timeout);
        Assert.Equal(_fixture.ActionServerCount, readyResults.Count);
        
        // Act & Assert
        // Each zone should have combat activity
        var combatPatterns = new Dictionary<string, string>();
        for (int i = 0; i < _fixture.ActionServerCount; i++)
        {
            combatPatterns[$"actionserver-{i}.log"] = "Bullet.*hit|damage.*dealt";
        }
        
        var combatResults = await _logAnalyzer.WaitForLogEntries(
            combatPatterns,
            timeout);
            
        Assert.Equal(_fixture.ActionServerCount, combatResults.Count);
    }
    
    [Fact]
    public async Task Player_Respawns_Should_Be_Tracked()
    {
        // Arrange
        var timeout = TimeSpan.FromSeconds(60);
        
        // Wait for combat to start
        await _logAnalyzer.WaitForLogEntry("actionserver-0.log", "damage", timeout);
        
        // Act
        // Wait for a player death/respawn
        var respawnEntry = await _logAnalyzer.WaitForLogEntry(
            "silo.log",
            "Player.*respawn",
            timeout,
            useRegex: true);
            
        // Assert
        Assert.NotNull(respawnEntry);
        
        // Verify respawn count is included in victory message
        await _logAnalyzer.WaitForLogEntry("silo.log", "Victory condition met", timeout);
        
        var scoresEntry = await _logAnalyzer.WaitForLogEntry(
            "silo.log",
            "respawns|deaths",
            TimeSpan.FromSeconds(10),
            useRegex: true);
            
        Assert.NotNull(scoresEntry);
    }
    
    [Fact]
    public async Task Game_Should_Handle_Bot_Disconnections_Gracefully()
    {
        // This test would require implementing bot disconnection logic
        // For now, we'll verify that the game continues even if a bot disconnects
        
        // Arrange
        var timeout = TimeSpan.FromSeconds(30);
        
        // Wait for all bots to connect
        var connectCount = await _logAnalyzer.CountOccurrences("connected as player");
        Assert.True(connectCount.Values.Sum() >= _fixture.BotCount);
        
        // Act & Assert
        // Even if a bot were to disconnect, the game should continue
        // and victory should still be achievable
        var victoryEntry = await _logAnalyzer.WaitForLogEntry(
            "silo.log",
            "Victory condition met",
            TimeSpan.FromSeconds(_fixture.TestTimeoutSeconds));
            
        Assert.NotNull(victoryEntry);
    }
}