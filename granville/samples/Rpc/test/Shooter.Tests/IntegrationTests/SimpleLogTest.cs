using Xunit;
using Xunit.Abstractions;

namespace Shooter.Tests.IntegrationTests;

/// <summary>
/// Simple test to verify basic functionality without starting the full application.
/// </summary>
public class SimpleLogTest
{
    private readonly ITestOutputHelper _output;
    
    public SimpleLogTest(ITestOutputHelper output)
    {
        _output = output;
    }
    
    [Fact]
    public void Log_Analyzer_Should_Parse_Log_Lines()
    {
        // This is a simple test that doesn't require starting services
        _output.WriteLine("Running simple log test");
        
        var logLine = "2024-01-15 10:30:45.123 [INFO ] TestCategory: Test message";
        var match = System.Text.RegularExpressions.Regex.Match(
            logLine, 
            @"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}) \[(\w+)\s*\] (.+?): (.+)$");
        
        Assert.True(match.Success);
        Assert.Equal("INFO", match.Groups[2].Value.Trim());
        Assert.Equal("TestCategory", match.Groups[3].Value);
        Assert.Equal("Test message", match.Groups[4].Value);
    }
    
    [Fact]
    public void Chat_Message_Regex_Should_Extract_Sender_And_Message()
    {
        var logMessage = "Received chat message from Game System: 🎉 Victory! All enemies have been defeated!";
        var match = System.Text.RegularExpressions.Regex.Match(
            logMessage, 
            @"Received chat message from (.+?): (.+)");
        
        Assert.True(match.Success);
        Assert.Equal("Game System", match.Groups[1].Value);
        Assert.Equal("🎉 Victory! All enemies have been defeated!", match.Groups[2].Value);
    }
}