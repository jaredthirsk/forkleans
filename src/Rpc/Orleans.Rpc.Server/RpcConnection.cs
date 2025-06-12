using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Forkleans.CodeGeneration;
using Forkleans.Configuration;
using Forkleans.Rpc.Transport;
using Forkleans.Runtime;
using Forkleans.Runtime.Messaging;
using Forkleans.Serialization.Invocation;
using Forkleans.Utilities;

namespace Forkleans.Rpc
{
    /// <summary>
    /// Represents a datagram-based RPC connection.
    /// Unlike Orleans' Connection class which reads from a PipeReader,
    /// this handles discrete messages received from the transport.
    /// </summary>
    internal sealed class RpcConnection : IDisposable
    {
        private readonly ILogger<RpcConnection> _logger;
        private readonly RpcCatalog _catalog;
        private readonly MessageFactory _messageFactory;
        private readonly MessagingOptions _messagingOptions;
        private readonly IPEndPoint _remoteEndPoint;
        private readonly string _connectionId;
        private readonly IRpcTransport _transport;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly ConcurrentDictionary<Guid, Protocol.RpcRequest> _pendingRequests = new();
        private readonly InterfaceToImplementationMappingCache _interfaceToImplementationMapping;
        
        private int _disposed;

        public RpcConnection(
            string connectionId,
            IPEndPoint remoteEndPoint,
            IRpcTransport transport,
            RpcCatalog catalog,
            MessageFactory messageFactory,
            MessagingOptions messagingOptions,
            InterfaceToImplementationMappingCache interfaceToImplementationMapping,
            ILogger<RpcConnection> logger)
        {
            _connectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
            _remoteEndPoint = remoteEndPoint ?? throw new ArgumentNullException(nameof(remoteEndPoint));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _messageFactory = messageFactory ?? throw new ArgumentNullException(nameof(messageFactory));
            _messagingOptions = messagingOptions ?? throw new ArgumentNullException(nameof(messagingOptions));
            _interfaceToImplementationMapping = interfaceToImplementationMapping ?? throw new ArgumentNullException(nameof(interfaceToImplementationMapping));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cancellationTokenSource = new CancellationTokenSource();
        }

        /// <summary>
        /// Gets the connection ID.
        /// </summary>
        public string ConnectionId => _connectionId;

        /// <summary>
        /// Gets the remote endpoint.
        /// </summary>
        public IPEndPoint RemoteEndPoint => _remoteEndPoint;

        /// <summary>
        /// Processes an incoming RPC request message.
        /// </summary>
        public async Task ProcessRequestAsync(Protocol.RpcRequest request)
        {
            if (_disposed != 0)
            {
                _logger.LogWarning("Ignoring request on disposed connection {ConnectionId}", _connectionId);
                return;
            }

            try
            {
                _logger.LogInformation("Processing RPC request {MessageId} for grain {GrainId} method {MethodId}",
                    request.MessageId, request.GrainId, request.MethodId);
                
                _logger.LogDebug("Request details - InterfaceType: {InterfaceType}, Arguments length: {ArgsLength}",
                    request.InterfaceType, request.Arguments?.Length ?? 0);

                // For now, let's use a simpler approach - invoke the grain method directly
                // This bypasses Orleans' message pump but is simpler for RPC
                var result = await InvokeGrainMethodAsync(request);
                
                _logger.LogInformation("Method invocation completed for request {MessageId}, preparing response", request.MessageId);
                
                // Send success response
                var response = new Protocol.RpcResponse
                {
                    RequestId = request.MessageId,
                    Success = true,
                    Payload = SerializeResult(result)
                };
                
                _logger.LogInformation("Sending success response for request {MessageId}", request.MessageId);
                await SendResponseAsync(response);
                _logger.LogInformation("Response sent successfully for request {MessageId}", request.MessageId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing request {MessageId}: {ErrorMessage}. Stack trace: {StackTrace}", 
                    request.MessageId, ex.Message, ex.StackTrace);
                
                // Send error response
                var errorResponse = new Protocol.RpcResponse
                {
                    RequestId = request.MessageId,
                    Success = false,
                    ErrorMessage = ex.Message
                };
                
                _logger.LogInformation("Sending error response for request {MessageId}", request.MessageId);
                await SendResponseAsync(errorResponse);
            }
        }

        /// <summary>
        /// Sends a response message to the remote endpoint.
        /// </summary>
        public async Task SendResponseAsync(Protocol.RpcResponse response)
        {
            try
            {
                var messageSerializer = _catalog.ServiceProvider.GetRequiredService<Protocol.RpcMessageSerializer>();
                var responseData = messageSerializer.SerializeMessage(response);
                await _transport.SendAsync(_remoteEndPoint, responseData, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send response {MessageId}", response.MessageId);
            }
        }

        /// <summary>
        /// Creates an Orleans Message from an RPC request.
        /// </summary>
        private Message CreateOrleansMessage(Protocol.RpcRequest request)
        {
            // Create a MethodRequest as the body object
            var methodRequest = new MethodRequest
            {
                InterfaceType = request.InterfaceType,
                MethodId = request.MethodId,
                Arguments = DeserializeArguments(request.Arguments)
            };

            // Create the message
            var message = _messageFactory.CreateMessage(
                methodRequest,
                InvokeMethodOptions.None);

            // Set message properties
            message.Id = CorrelationId.GetNext();
            message.TargetGrain = request.GrainId;
            message.InterfaceType = request.InterfaceType;
            message.IsReadOnly = false;
            message.IsUnordered = false;

            return message;
        }

        private object[] DeserializeArguments(byte[] serializedArguments)
        {
            if (serializedArguments == null || serializedArguments.Length == 0)
            {
                return Array.Empty<object>();
            }

            // TODO: Implement proper deserialization
            // For now, assume the arguments are already deserialized or empty
            return Array.Empty<object>();
        }

        private async Task<object> InvokeGrainMethodAsync(Protocol.RpcRequest request)
        {
            _logger.LogInformation("Starting grain method invocation for {GrainId}", request.GrainId);
            
            _logger.LogInformation("About to call _catalog.GetOrCreateActivationAsync for {GrainId}", request.GrainId);
            // Get or create the grain activation
            var grainContext = await _catalog.GetOrCreateActivationAsync(request.GrainId);
            _logger.LogInformation("Got grain context for {GrainId}, GrainInstance type: {GrainType}", 
                request.GrainId, grainContext.GrainInstance?.GetType().Name ?? "null");
            
            var grain = grainContext.GrainInstance;
            
            if (grain == null)
            {
                throw new InvalidOperationException($"Grain instance not found for {request.GrainId}");
            }
            
            // For a simplified RPC approach, we'll use reflection to invoke the method
            // In a production system, you'd want to use the generated invokables
            
            // Get the interface type - need to resolve the actual Type from GrainInterfaceType
            var grainType = grain.GetType();
            _logger.LogDebug("Grain type: {GrainType}, interfaces: {Interfaces}", 
                grainType.Name, string.Join(", ", grainType.GetInterfaces().Select(i => i.Name)));
            
            // Find the grain interface on the implementation
            Type interfaceType = null;
            foreach (var iface in grainType.GetInterfaces())
            {
                // Check if this is a grain interface (inherits from IGrain but not the base types)
                if (!iface.IsClass && 
                    typeof(IGrain).IsAssignableFrom(iface) &&
                    iface != typeof(IGrainObserver) && 
                    iface != typeof(IAddressable) && 
                    iface != typeof(IGrainExtension) &&
                    iface != typeof(IGrain) && 
                    iface != typeof(IGrainWithGuidKey) && 
                    iface != typeof(IGrainWithIntegerKey) &&
                    iface != typeof(IGrainWithGuidCompoundKey) && 
                    iface != typeof(IGrainWithIntegerCompoundKey) &&
                    iface != typeof(ISystemTarget))
                {
                    _logger.LogDebug("Found grain interface: {Interface}", iface.Name);
                    interfaceType = iface;
                    break;
                }
            }
            
            if (interfaceType == null)
            {
                throw new InvalidOperationException($"No grain interface found on type {grainType.Name}");
            }
            
            // Get methods sorted alphabetically by name for consistent ordering
            var methods = interfaceType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => !m.IsSpecialName) // Exclude property getters/setters
                .OrderBy(m => m.Name, StringComparer.Ordinal)
                .ToArray();
            
            _logger.LogDebug("Interface {Interface} has {MethodCount} methods: {Methods}", 
                interfaceType.Name, methods.Length, string.Join(", ", methods.Select(m => m.Name)));
            
            if (request.MethodId >= methods.Length)
            {
                throw new InvalidOperationException($"Method ID {request.MethodId} not found on interface {interfaceType.Name}. Interface has {methods.Length} methods.");
            }
            
            var method = methods[request.MethodId];
            _logger.LogInformation("Invoking method {MethodName} (ID: {MethodId}) on grain {GrainId}", 
                method.Name, request.MethodId, request.GrainId);
            
            // Deserialize arguments
            object[] arguments = null;
            var parameters = method.GetParameters();
            
            if (request.Arguments != null && request.Arguments.Length > 0)
            {
                try
                {
                    var jsonString = System.Text.Encoding.UTF8.GetString(request.Arguments);
                    _logger.LogDebug("Deserializing arguments: {Json}", jsonString);
                    
                    var jsonArray = JsonSerializer.Deserialize<JsonElement[]>(request.Arguments);
                    arguments = new object[parameters.Length];
                    
                    for (int i = 0; i < Math.Min(jsonArray.Length, parameters.Length); i++)
                    {
                        var paramType = parameters[i].ParameterType;
                        arguments[i] = JsonSerializer.Deserialize(jsonArray[i].GetRawText(), paramType);
                        _logger.LogDebug("Argument {Index}: {Value} (Type: {Type})", i, arguments[i], paramType.Name);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to deserialize arguments for method {Method}", method.Name);
                    throw new InvalidOperationException($"Failed to deserialize arguments: {ex.Message}", ex);
                }
            }
            else
            {
                arguments = new object[parameters.Length];
                _logger.LogDebug("No arguments provided for method {Method}", method.Name);
            }
            
            try
            {
                // Find the implementation method
                var implementationMap = _interfaceToImplementationMapping.GetOrCreate(grainType, interfaceType);
                if (!implementationMap.TryGetValue(method, out var methodEntry))
                {
                    throw new InvalidOperationException($"Implementation not found for method {method.Name}");
                }
                
                // Invoke the method
                _logger.LogDebug("Invoking implementation method {Method} on grain {GrainType}", 
                    methodEntry.ImplementationMethod.Name, grainType.Name);
                    
                var result = methodEntry.ImplementationMethod.Invoke(grain, arguments);
                _logger.LogDebug("Method invocation returned, result type: {ResultType}", 
                    result?.GetType().Name ?? "null");
                
                // Handle async methods
                if (result is Task task)
                {
                    _logger.LogDebug("Result is a Task, awaiting...");
                    await task;
                    _logger.LogDebug("Task completed");
                    
                    // Get the result from Task<T> or ValueTask<T>
                    var taskType = task.GetType();
                    if (taskType.IsGenericType)
                    {
                        var genericDef = taskType.GetGenericTypeDefinition();
                        if (genericDef == typeof(Task<>) || genericDef == typeof(ValueTask<>))
                        {
                            var resultProperty = taskType.GetProperty("Result");
                            var taskResult = resultProperty.GetValue(task);
                            _logger.LogDebug("Method {Method} returned: {Result}", method.Name, taskResult);
                            return taskResult;
                        }
                    }
                    
                    _logger.LogDebug("Method {Method} returned void", method.Name);
                    return null; // Task without result
                }
                else if (result != null && result.GetType().IsGenericType && 
                         result.GetType().GetGenericTypeDefinition() == typeof(ValueTask<>))
                {
                    // Handle ValueTask<T>
                    var asTaskMethod = result.GetType().GetMethod("AsTask");
                    var task2 = (Task)asTaskMethod.Invoke(result, null);
                    await task2;
                    
                    var resultProperty = task2.GetType().GetProperty("Result");
                    var taskResult = resultProperty.GetValue(task2);
                    _logger.LogDebug("Method {Method} returned ValueTask result: {Result}", method.Name, taskResult);
                    return taskResult;
                }
                
                _logger.LogDebug("Method {Method} returned synchronously: {Result}", method.Name, result);
                return result;
            }
            catch (TargetInvocationException tie)
            {
                throw tie.InnerException ?? tie;
            }
        }
        
        private byte[] SerializeResult(object result)
        {
            if (result == null)
            {
                return Array.Empty<byte>();
            }
            
            // For now, use JSON serialization for results
            var json = System.Text.Json.JsonSerializer.Serialize(result);
            return System.Text.Encoding.UTF8.GetBytes(json);
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
            {
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _logger.LogDebug("RPC connection {ConnectionId} disposed", _connectionId);
            }
        }
    }

}