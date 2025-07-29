#!/usr/bin/env pwsh

# Simple test to diagnose RPC connection and GetGrain issues

Write-Host "Creating RPC connection test program..." -ForegroundColor Green

$testProgram = @'
using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Granville.Rpc;
using Granville.Rpc.Hosting;
using Granville.Rpc.Transport.LiteNetLib;
using Orleans;
using Orleans.Metadata;
using Orleans.Runtime;
using Shooter.Shared.RpcInterfaces;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Starting RPC connection test...");
        
        var serverEndpoint = new IPEndPoint(IPAddress.Loopback, 12000);
        Console.WriteLine($"Connecting to RPC server at {serverEndpoint}");
        
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Debug);
                logging.AddFilter("Granville.Rpc", LogLevel.Debug);
                logging.AddFilter("Orleans", LogLevel.Debug);
            })
            .UseOrleansRpcClient(rpc =>
            {
                rpc.ConnectTo(serverEndpoint.Address.ToString(), serverEndpoint.Port);
                rpc.UseLiteNetLib();
            })
            .ConfigureServices(services =>
            {
                services.AddSerializer(serializer =>
                {
                    serializer.AddAssembly(typeof(IGameRpcGrain).Assembly);
                    serializer.AddAssembly(typeof(Granville.Rpc.Protocol.RpcMessage).Assembly);
                });
            })
            .Build();
            
        try
        {
            Console.WriteLine("Starting host...");
            await host.StartAsync();
            Console.WriteLine("Host started successfully");
            
            // Get the RPC client
            var rpcClient = host.Services.GetRequiredService<IRpcClient>();
            Console.WriteLine($"RPC client obtained: {rpcClient.GetType().FullName}");
            
            // Check manifest provider
            try
            {
                var manifestProvider = host.Services.GetKeyedService<IClusterManifestProvider>("rpc");
                Console.WriteLine($"Manifest provider type: {manifestProvider?.GetType().FullName ?? "NULL"}");
                
                if (manifestProvider != null)
                {
                    var manifest = manifestProvider.Current;
                    Console.WriteLine($"Current manifest version: {manifest?.Version}");
                    Console.WriteLine($"Grain count: {manifest?.AllGrainManifests?.Sum(m => m.Grains.Count) ?? 0}");
                    Console.WriteLine($"Interface count: {manifest?.AllGrainManifests?.Sum(m => m.Interfaces.Count) ?? 0}");
                    
                    // List available grain types
                    if (manifest?.AllGrainManifests != null)
                    {
                        foreach (var silo in manifest.AllGrainManifests)
                        {
                            foreach (var grain in silo.Grains)
                            {
                                Console.WriteLine($"  Grain: {grain.Key}");
                                foreach (var prop in grain.Value.Properties)
                                {
                                    if (prop.Key.StartsWith("interface."))
                                    {
                                        Console.WriteLine($"    Interface: {prop.Value}");
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking manifest: {ex.Message}");
            }
            
            // Try to get grain factory
            try
            {
                var grainFactory = host.Services.GetKeyedService<IGrainFactory>("rpc");
                Console.WriteLine($"Grain factory type: {grainFactory?.GetType().FullName ?? "NULL"}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting grain factory: {ex.Message}");
            }
            
            // Wait a bit for manifest to be populated
            Console.WriteLine("\nWaiting 2 seconds for manifest exchange...");
            await Task.Delay(2000);
            
            // Try to get a grain
            Console.WriteLine("\nAttempting to get IGameRpcGrain...");
            try
            {
                var grain = rpcClient.GetGrain<IGameRpcGrain>("game");
                Console.WriteLine($"SUCCESS: Got grain reference: {grain?.GetType().FullName ?? "NULL"}");
                
                // Try to call a method
                Console.WriteLine("\nTrying to call GetServerInfo()...");
                var serverInfo = await grain.GetServerInfo();
                Console.WriteLine($"Server info: {serverInfo}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR getting grain: {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                }
            }
            
            await host.StopAsync();
            Console.WriteLine("\nTest completed");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FATAL ERROR: {ex}");
            Environment.Exit(1);
        }
    }
}
'@

# Create test directory
$testDir = "$PSScriptRoot/TestRpcConnection"
if (!(Test-Path $testDir)) {
    New-Item -ItemType Directory -Path $testDir -Force | Out-Null
}

# Save test program
$testProgram | Out-File -FilePath "$testDir/Program.cs" -Encoding UTF8

# Create project file
$projectFile = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Shooter.Shared\Shooter.Shared.csproj" />
    <PackageReference Include="Granville.Rpc.Client" />
    <PackageReference Include="Granville.Rpc.Transport.LiteNetLib" />
    <PackageReference Include="Granville.Orleans.Core.Abstractions" />
    <PackageReference Include="Granville.Orleans.Serialization" />
    <PackageReference Include="Microsoft.Extensions.Hosting" />
  </ItemGroup>
</Project>
'@

$projectFile | Out-File -FilePath "$testDir/TestRpcConnection.csproj" -Encoding UTF8

Write-Host "`nBuilding test program..." -ForegroundColor Yellow
Push-Location $testDir
try {
    dotnet-win restore
    dotnet-win build -c Debug
    
    Write-Host "`nEnsure an ActionServer is running on port 12000, then run:" -ForegroundColor Cyan
    Write-Host "dotnet-win run --project $testDir/TestRpcConnection.csproj" -ForegroundColor White
}
finally {
    Pop-Location
}