using System;
using System.Threading.Tasks;
using Shooter.Shared.RpcInterfaces;

namespace TestProxyArgs
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Testing proxy argument passing...");
            
            // This would simulate what the bot is doing
            string playerId = "test-player-id-12345";
            Console.WriteLine($"PlayerId before call: '{playerId}' (type: {playerId?.GetType()?.FullName ?? "null"})");
            
            // In reality, _gameGrain.ConnectPlayer(playerId) would be called
            // But we can't test that without a full setup
            
            Console.WriteLine("Test complete.");
        }
    }
}