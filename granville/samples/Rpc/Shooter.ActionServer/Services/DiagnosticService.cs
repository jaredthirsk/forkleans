using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Shooter.ActionServer.Services;

/// <summary>
/// Diagnostic service to check service registrations
/// </summary>
public class DiagnosticService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DiagnosticService> _logger;

    public DiagnosticService(IServiceProvider serviceProvider, ILogger<DiagnosticService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogWarning("=== SERVICE DIAGNOSTICS ===");
        
        // Check which GrainFactory is registered
        var grainFactory = _serviceProvider.GetService<Orleans.IGrainFactory>();
        _logger.LogWarning("IGrainFactory type: {Type}", grainFactory?.GetType().FullName ?? "NULL");
        
        // Check which IClusterClient is registered
        var clusterClient = _serviceProvider.GetService<Orleans.IClusterClient>();
        _logger.LogWarning("IClusterClient type: {Type}", clusterClient?.GetType().FullName ?? "NULL");
        
        // Check if IRpcClient is registered
        var rpcClient = _serviceProvider.GetService<Granville.Rpc.IRpcClient>();
        _logger.LogWarning("IRpcClient type: {Type}", rpcClient?.GetType().FullName ?? "NULL");
        
        // Check all GrainReferenceActivatorProviders
        var providers = _serviceProvider.GetServices<Orleans.GrainReferences.IGrainReferenceActivatorProvider>();
        foreach (var provider in providers)
        {
            _logger.LogWarning("GrainReferenceActivatorProvider: {Type}", provider.GetType().FullName);
        }
        
        // Check manifest providers
        var manifestProvider = _serviceProvider.GetService<Orleans.Runtime.IClusterManifestProvider>();
        _logger.LogWarning("IClusterManifestProvider type: {Type}", manifestProvider?.GetType().FullName ?? "NULL");
        
        // Check keyed manifest providers
        var orleansManifest = _serviceProvider.GetKeyedService<Orleans.Runtime.IClusterManifestProvider>("orleans");
        _logger.LogWarning("IClusterManifestProvider[orleans] type: {Type}", orleansManifest?.GetType().FullName ?? "NULL");
        
        var rpcManifest = _serviceProvider.GetKeyedService<Orleans.Runtime.IClusterManifestProvider>("rpc");
        _logger.LogWarning("IClusterManifestProvider[rpc] type: {Type}", rpcManifest?.GetType().FullName ?? "NULL");
        
        _logger.LogWarning("=== END DIAGNOSTICS ===");
        
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}