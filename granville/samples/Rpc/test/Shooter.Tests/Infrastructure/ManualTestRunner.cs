using System.Diagnostics;

namespace Shooter.Tests.Infrastructure;

/// <summary>
/// Manual test runner that helps start services individually for testing.
/// This is an alternative to the full Aspire AppHost approach.
/// </summary>
public static class ManualTestRunner
{
    public static Task<string> RunManualTest()
    {
        var logDir = Path.Combine(Directory.GetCurrentDirectory(), "test-logs");
        Directory.CreateDirectory(logDir);
        
        var result = "Manual Test Instructions:\n";
        result += "========================\n\n";
        
        result += "To manually test the chat system:\n\n";
        
        result += "1. Start the Silo:\n";
        result += "   cd Shooter.Silo\n";
        result += "   dotnet run\n\n";
        
        result += "2. Start ActionServers (in separate terminals):\n";
        result += "   cd Shooter.ActionServer\n";
        result += "   dotnet run --urls http://localhost:7072\n";
        result += "   dotnet run --urls http://localhost:7073\n\n";
        
        result += "3. Start Bots (in separate terminals):\n";
        result += "   cd Shooter.Bot\n";
        result += "   dotnet run\n\n";
        
        result += "4. Monitor the logs:\n";
        result += "   tail -f ../logs/*.log | grep -i \"chat\\|victory\"\n\n";
        
        result += "Expected behavior:\n";
        result += "- Bots will destroy all enemies\n";
        result += "- Victory condition will be detected\n";
        result += "- Chat messages will be broadcast\n";
        result += "- All bots will receive the messages\n";
        
        return Task.FromResult(result);
    }
    
    public static Task<bool> CheckPrerequisites()
    {
        // Check if we can find the project directories
        var currentDir = Directory.GetCurrentDirectory();
        var rpcDir = currentDir;
        
        while (!string.IsNullOrEmpty(rpcDir) && Path.GetFileName(rpcDir) != "Rpc")
        {
            rpcDir = Path.GetDirectoryName(rpcDir);
        }
        
        if (string.IsNullOrEmpty(rpcDir))
        {
            return Task.FromResult(false);
        }
        
        var requiredProjects = new[] { "Shooter.Silo", "Shooter.ActionServer", "Shooter.Bot" };
        foreach (var project in requiredProjects)
        {
            var projectPath = Path.Combine(rpcDir, project);
            if (!Directory.Exists(projectPath))
            {
                Console.WriteLine($"Missing required project: {projectPath}");
                return Task.FromResult(false);
            }
        }
        
        return Task.FromResult(true);
    }
}