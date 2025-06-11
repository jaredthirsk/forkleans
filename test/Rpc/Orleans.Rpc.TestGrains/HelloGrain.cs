using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Forkleans;
using Forkleans.Rpc.TestGrainInterfaces;

namespace Forkleans.Rpc.TestGrains
{
    /// <summary>
    /// Implementation of the Hello grain for RPC testing.
    /// </summary>
    public class HelloGrain : Grain, IHelloGrain
    {
        private readonly ILogger<HelloGrain> _logger;

        public HelloGrain(ILogger<HelloGrain> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public override Task OnActivateAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("HelloGrain {GrainId} activated", this.GetPrimaryKeyString());
            return base.OnActivateAsync(cancellationToken);
        }

        public ValueTask<string> SayHello(string name)
        {
            _logger.LogInformation("SayHello called with name: {Name}", name);
            
            if (string.IsNullOrWhiteSpace(name))
            {
                return new ValueTask<string>("Hello, anonymous!");
            }

            return new ValueTask<string>($"Hello, {name}! Greetings from grain {this.GetPrimaryKeyString()}");
        }

        public ValueTask<string> Echo(string message)
        {
            _logger.LogDebug("Echo called with message: {Message}", message);
            return new ValueTask<string>(message);
        }

        public ValueTask<HelloResponse> GetDetailedGreeting(HelloRequest request)
        {
            _logger.LogInformation("GetDetailedGreeting called for {Name} from {Location}", 
                request.Name, request.Location);

            var response = new HelloResponse
            {
                Greeting = $"Hello {request.Name}, age {request.Age} from {request.Location}! " +
                          $"This is grain {this.GetPrimaryKeyString()} speaking.",
                ServerTime = DateTime.UtcNow.ToString("O"),
                ProcessId = Process.GetCurrentProcess().Id
            };

            return new ValueTask<HelloResponse>(response);
        }

        public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
        {
            _logger.LogInformation("HelloGrain {GrainId} deactivating. Reason: {Reason}", 
                this.GetPrimaryKeyString(), reason);
            return base.OnDeactivateAsync(reason, cancellationToken);
        }
    }
}