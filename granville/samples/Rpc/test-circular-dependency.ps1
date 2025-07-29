#!/usr/bin/env pwsh

# Test script to verify the circular dependency fix

Write-Host "Testing RPC client circular dependency fix..." -ForegroundColor Green

# Change to the Shooter sample directory
Set-Location "$PSScriptRoot/Shooter"

# Build the project
Write-Host "`nBuilding Shooter sample..." -ForegroundColor Yellow
dotnet-win build -c Debug

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "`nBuild succeeded!" -ForegroundColor Green

# Run a simple test to see if the client can start without timeout
Write-Host "`nTesting RPC client startup..." -ForegroundColor Yellow

# Create a simple test program
$testCode = @'
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Granville.Rpc.Hosting;
using Granville.Rpc.Transport.LiteNetLib;
using SampleBase.Interfaces.Rpc;

class Program
{
    static async Task Main(string[] args)
    {
        var builder = Host.CreateDefaultBuilder(args);
        
        builder.ConfigureServices(services =>
        {
            services.AddRpcClient(client =>
            {
                client.AddServerAddress(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 19001));
                client.AddServerAddress(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 19002));
                client.AddServerAddress(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 19003));
            });
            services.AddLiteNetLibRpcTransport();
        });

        var host = builder.Build();
        
        try
        {
            await host.StartAsync();
            
            // Try to get the RPC client and call GetGrain
            var rpcClient = host.Services.GetRequiredService<Granville.Rpc.IRpcClient>();
            Console.WriteLine("RPC Client obtained successfully!");
            
            // Try to get a grain reference (this would previously timeout)
            var helloGrain = rpcClient.GetGrain<IHelloGrainRpc>(Guid.NewGuid());
            Console.WriteLine("Grain reference obtained successfully!");
            
            await host.StopAsync();
            Console.WriteLine("Test passed!");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Test failed: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            Environment.Exit(1);
        }
    }
}
'@

# Save the test code
$testFile = "$PSScriptRoot/test-circular-dep.cs"
$testCode | Out-File -FilePath $testFile -Encoding UTF8

Write-Host "Running test program..." -ForegroundColor Yellow

# Run the test using dotnet script
dotnet-win script $testFile --isolated-load-context

$exitCode = $LASTEXITCODE

# Clean up
Remove-Item $testFile -ErrorAction SilentlyContinue

if ($exitCode -eq 0) {
    Write-Host "`nCircular dependency fix verified successfully!" -ForegroundColor Green
} else {
    Write-Host "`nCircular dependency test failed!" -ForegroundColor Red
}

exit $exitCode