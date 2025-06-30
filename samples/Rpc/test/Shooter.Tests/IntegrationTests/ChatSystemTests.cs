using Shooter.Tests.Infrastructure;
using Xunit;

namespace Shooter.Tests.IntegrationTests;

/// <summary>
/// Integration tests for the chat system functionality.
/// Verifies that chat messages are properly distributed from Silo to ActionServers to Clients/Bots.
/// </summary>
public class ChatSystemTests : IClassFixture<ShooterTestFixture>
{
    private readonly ShooterTestFixture _fixture;
    private readonly LogAnalyzer _logAnalyzer;
    
    public ChatSystemTests(ShooterTestFixture fixture)
    {
        _fixture = fixture;
        _logAnalyzer = new LogAnalyzer(_fixture.LogDirectory);
    }
    
    [Fact]
    public async Task Bots_Should_Receive_Victory_Chat_Messages()
    {
        // Arrange
        var timeout = TimeSpan.FromSeconds(_fixture.TestTimeoutSeconds);
        
        // Wait for bots to connect
        var botConnectPatterns = new Dictionary<string, string>();
        for (int i = 0; i < _fixture.BotCount; i++)
        {
            botConnectPatterns[$"bot-{i}.log"] = "connected as player";
        }
        
        var connectResults = await _logAnalyzer.WaitForLogEntries(botConnectPatterns, timeout);
        Assert.Equal(_fixture.BotCount, connectResults.Count);
        
        // Act & Assert
        // Wait for victory condition - the game should automatically achieve victory
        // when all enemies are destroyed (bots in test mode will destroy them)
        var victoryEntry = await _logAnalyzer.WaitForLogEntry(
            "silo.log",
            "Victory condition met",
            timeout);
            
        Assert.NotNull(victoryEntry);
        
        // Verify victory chat message is sent from Silo
        var broadcastEntry = await _logAnalyzer.WaitForLogEntry(
            "silo.log",
            "Broadcasting chat message from Game System: 🎉 Victory!",
            TimeSpan.FromSeconds(5));
            
        Assert.NotNull(broadcastEntry);
        
        // Verify all bots receive the victory message
        var botVictoryPatterns = new Dictionary<string, string>();
        for (int i = 0; i < _fixture.BotCount; i++)
        {
            botVictoryPatterns[$"bot-{i}.log"] = "Received chat message from Game System: 🎉 Victory!";
        }
        
        var victoryResults = await _logAnalyzer.WaitForLogEntries(
            botVictoryPatterns, 
            TimeSpan.FromSeconds(10));
            
        Assert.Equal(_fixture.BotCount, victoryResults.Count);
    }
    
    [Fact]
    public async Task Bots_Should_Receive_Game_Restart_Messages()
    {
        // Arrange
        var timeout = TimeSpan.FromSeconds(_fixture.TestTimeoutSeconds);
        
        // Wait for victory first
        var victoryEntry = await _logAnalyzer.WaitForLogEntry(
            "silo.log",
            "Victory condition met",
            timeout);
            
        Assert.NotNull(victoryEntry);
        
        // Act & Assert
        // Game should restart after 15 seconds
        var restartEntry = await _logAnalyzer.WaitForLogEntry(
            "silo.log",
            "Broadcasting chat message from Game System: Game restarted",
            TimeSpan.FromSeconds(20));
            
        Assert.NotNull(restartEntry);
        
        // Verify all bots receive the restart message
        var botRestartPatterns = new Dictionary<string, string>();
        for (int i = 0; i < _fixture.BotCount; i++)
        {
            botRestartPatterns[$"bot-{i}.log"] = "Received chat message from Game System: Game restarted";
        }
        
        var restartResults = await _logAnalyzer.WaitForLogEntries(
            botRestartPatterns,
            TimeSpan.FromSeconds(5));
            
        Assert.Equal(_fixture.BotCount, restartResults.Count);
    }
    
    [Fact]
    public async Task Chat_Messages_Should_Include_Player_Scores()
    {
        // Arrange
        var timeout = TimeSpan.FromSeconds(_fixture.TestTimeoutSeconds);
        
        // Wait for victory
        await _logAnalyzer.WaitForLogEntry("silo.log", "Victory condition met", timeout);
        
        // Act & Assert
        // Look for player scores message
        var scoresEntry = await _logAnalyzer.WaitForLogEntry(
            "silo.log",
            "Broadcasting chat message from Game System: Player Scores:",
            TimeSpan.FromSeconds(5));
            
        Assert.NotNull(scoresEntry);
        
        // Verify at least one bot receives the scores
        var scoreReceivedEntry = await _logAnalyzer.WaitForLogEntry(
            "bot-0.log",
            "Player Scores:",
            TimeSpan.FromSeconds(5));
            
        Assert.NotNull(scoreReceivedEntry);
    }
    
    [Fact]
    public async Task Chat_Messages_Should_Propagate_Across_All_Zones()
    {
        // Arrange
        var timeout = TimeSpan.FromSeconds(30);
        
        // Wait for ActionServers to be assigned zones
        var zoneAssignments = new Dictionary<string, string>();
        for (int i = 0; i < _fixture.ActionServerCount; i++)
        {
            zoneAssignments[$"actionserver-{i}.log"] = "Assigned to zone";
        }
        
        var assignmentResults = await _logAnalyzer.WaitForLogEntries(zoneAssignments, timeout);
        Assert.Equal(_fixture.ActionServerCount, assignmentResults.Count);
        
        // Act
        // Wait for any chat message to be broadcast
        var broadcastEntry = await _logAnalyzer.WaitForLogEntry(
            "silo.log",
            "Broadcasting chat message",
            timeout);
            
        Assert.NotNull(broadcastEntry);
        
        // Assert
        // Verify each ActionServer receives the message
        var actionServerPatterns = new Dictionary<string, string>();
        for (int i = 0; i < _fixture.ActionServerCount; i++)
        {
            actionServerPatterns[$"actionserver-{i}.log"] = "Received chat message";
        }
        
        var actionServerResults = await _logAnalyzer.WaitForLogEntries(
            actionServerPatterns,
            TimeSpan.FromSeconds(10));
            
        Assert.Equal(_fixture.ActionServerCount, actionServerResults.Count);
    }
    
    [Fact]
    public async Task Multiple_Chat_Messages_Should_Be_Delivered_In_Order()
    {
        // Arrange
        var timeout = TimeSpan.FromSeconds(_fixture.TestTimeoutSeconds);
        
        // Wait for victory which triggers multiple messages
        await _logAnalyzer.WaitForLogEntry("silo.log", "Victory condition met", timeout);
        
        // Act
        // Extract all chat messages from a bot log
        await Task.Delay(2000); // Give time for all messages to arrive
        var chatMessages = await _logAnalyzer.ExtractChatMessages("bot-0.log");
        
        // Assert
        Assert.NotEmpty(chatMessages);
        
        // Victory message should come before restart message
        var victoryMessage = chatMessages.FirstOrDefault(m => m.Message.Contains("Victory!"));
        var restartMessage = chatMessages.FirstOrDefault(m => m.Message.Contains("Game restarted"));
        
        if (victoryMessage != null && restartMessage != null)
        {
            Assert.True(victoryMessage.Timestamp < restartMessage.Timestamp,
                "Victory message should arrive before restart message");
        }
    }
}