using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Forkleans.CodeGeneration;
using Forkleans.Configuration;
using Forkleans.GrainReferences;
using Forkleans.Messaging;
using Forkleans.Runtime;
using Forkleans.Serialization;
using Forkleans.Serialization.Invocation;

namespace Forkleans.Rpc
{
    /// <summary>
    /// RPC-specific implementation of IRuntimeClient for outside (client) use.
    /// </summary>
    internal sealed class OutsideRpcRuntimeClient : IRuntimeClient
    {
        private readonly ILogger<OutsideRpcRuntimeClient> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IGrainReferenceRuntime _grainReferenceRuntime;
        private readonly TimeProvider _timeProvider;
        private readonly ILocalClientDetails _clientDetails;
        private readonly RpcClient _rpcClient;
        private TimeSpan _responseTimeout = TimeSpan.FromSeconds(30);

        public OutsideRpcRuntimeClient(
            IServiceProvider serviceProvider,
            ILogger<OutsideRpcRuntimeClient> logger,
            IGrainReferenceRuntime grainReferenceRuntime,
            TimeProvider timeProvider,
            ILocalClientDetails clientDetails,
            RpcClient rpcClient)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _grainReferenceRuntime = grainReferenceRuntime ?? throw new ArgumentNullException(nameof(grainReferenceRuntime));
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            _clientDetails = clientDetails ?? throw new ArgumentNullException(nameof(clientDetails));
            _rpcClient = rpcClient ?? throw new ArgumentNullException(nameof(rpcClient));
        }

        public TimeProvider TimeProvider => _timeProvider;

        public IInternalGrainFactory InternalGrainFactory => _serviceProvider.GetRequiredService<IGrainFactory>() as IInternalGrainFactory;

        public string CurrentActivationIdentity => _clientDetails.ClientId;

        public IServiceProvider ServiceProvider => _serviceProvider;

        public IGrainReferenceRuntime GrainReferenceRuntime => _grainReferenceRuntime;

        public TimeSpan GetResponseTimeout() => _responseTimeout;

        public void SetResponseTimeout(TimeSpan timeout) => _responseTimeout = timeout;

        public void SendRequest(GrainReference target, IInvokable request, IResponseCompletionSource context, InvokeMethodOptions options)
        {
            _logger.LogDebug("SendRequest called for {Target} method {MethodName}", target, request.GetMethodName());
            
            Task.Run(async () =>
            {
                try
                {
                    // Create RPC request
                    var methodId = GetMethodId(target.InterfaceType, request.GetMethodName());
                    var rpcRequest = new Protocol.RpcRequest
                    {
                        MessageId = Guid.NewGuid(),
                        GrainId = target.GrainId,
                        InterfaceType = target.InterfaceType,
                        MethodId = methodId,
                        Arguments = SerializeArguments(request)
                    };
                    
                    _logger.LogDebug("Sending RPC request {MessageId} to grain {GrainId}", rpcRequest.MessageId, rpcRequest.GrainId);
                    
                    // Send request and wait for response
                    var response = await _rpcClient.SendRequestAsync(rpcRequest);
                    
                    _logger.LogDebug("Received RPC response for {MessageId}: Success={Success}", rpcRequest.MessageId, response.Success);
                    
                    if (response.Success)
                    {
                        // Deserialize the result
                        object result = null;
                        if (response.Payload != null && response.Payload.Length > 0)
                        {
                            var json = System.Text.Encoding.UTF8.GetString(response.Payload);
                            result = System.Text.Json.JsonSerializer.Deserialize<object>(json);
                        }
                        
                        context.Complete(Response.FromResult(result));
                    }
                    else
                    {
                        context.Complete(Response.FromException(new Exception(response.ErrorMessage ?? "Unknown error")));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending RPC request");
                    context.Complete(Response.FromException(ex));
                }
            });
        }
        
        private byte[] SerializeArguments(IInvokable request)
        {
            var argCount = request.GetArgumentCount();
            if (argCount == 0)
            {
                return Array.Empty<byte>();
            }
            
            var args = new object[argCount];
            for (int i = 0; i < argCount; i++)
            {
                args[i] = request.GetArgument(i);
            }
            
            var json = System.Text.Json.JsonSerializer.Serialize(args);
            return System.Text.Encoding.UTF8.GetBytes(json);
        }
        
        private int GetMethodId(GrainInterfaceType interfaceType, string methodName)
        {
            // Find the actual interface type
            // This is a simplified approach - in production, you'd want to cache this
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            Type interfaceClrType = null;
            
            // GrainInterfaceType.Value is an IdSpan, convert to string
            var typeName = interfaceType.Value.ToString();
            _logger.LogDebug("Looking for interface type: {TypeName}", typeName);
            
            foreach (var assembly in assemblies)
            {
                try
                {
                    var types = assembly.GetTypes();
                    foreach (var type in types)
                    {
                        if (type.IsInterface && 
                            (type.Name == typeName || 
                             type.FullName == typeName ||
                             type.FullName.EndsWith("." + typeName)))
                        {
                            interfaceClrType = type;
                            _logger.LogDebug("Found interface type {TypeName} in assembly {Assembly}", type.FullName, assembly.FullName);
                            break;
                        }
                    }
                    if (interfaceClrType != null) break;
                }
                catch (Exception ex)
                {
                    // Some assemblies might not be loadable
                    _logger.LogTrace(ex, "Could not load types from assembly {Assembly}", assembly.FullName);
                }
            }
            
            if (interfaceClrType == null)
            {
                _logger.LogWarning("Could not find interface type {InterfaceType}, using method name hash", interfaceType);
                return Math.Abs(methodName.GetHashCode()) % 100; // Fallback
            }
            
            // Get methods sorted alphabetically (same as server)
            var methods = interfaceClrType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => !m.IsSpecialName)
                .OrderBy(m => m.Name, StringComparer.Ordinal)
                .ToArray();
            
            _logger.LogDebug("Interface {Interface} has methods: {Methods}", 
                interfaceClrType.Name, string.Join(", ", methods.Select(m => m.Name)));
            
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name == methodName)
                {
                    _logger.LogInformation("Method {MethodName} on {Interface} has ID {MethodId}", methodName, interfaceType, i);
                    return i;
                }
            }
            
            throw new InvalidOperationException($"Method {methodName} not found on interface {interfaceType}");
        }

        public void SendResponse(Message request, Response response)
        {
            throw new NotSupportedException("Client does not send responses");
        }

        public void ReceiveResponse(Message message)
        {
            throw new NotImplementedException("RPC response handling not yet implemented");
        }

        public IAddressable CreateObjectReference(IAddressable obj)
        {
            throw new NotSupportedException("Object references are not supported in RPC mode");
        }

        public void DeleteObjectReference(IAddressable obj)
        {
            // Not supported in RPC mode
        }

        public void BreakOutstandingMessagesToSilo(SiloAddress deadSilo)
        {
            // Not applicable for RPC
        }

        public int GetRunningRequestsCount(GrainInterfaceType grainInterfaceType)
        {
            // For testing purposes - always return 0
            return 0;
        }
    }
}