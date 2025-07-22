using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.CodeGeneration;
using Orleans.Configuration;
using Granville.Rpc.Transport;
using Orleans.Runtime;
using Orleans.Runtime.Messaging;
using Orleans.Serialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Invocation;
using Orleans.Serialization.Session;
using Orleans.Utilities;

namespace Granville.Rpc
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
        private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _activeAsyncEnumerables = new();
        private readonly InterfaceToImplementationMappingCache _interfaceToImplementationMapping;
        private readonly Serializer _serializer;
        private readonly RpcSerializationSessionFactory _sessionFactory;

        private int _disposed;

        public RpcConnection(
            string connectionId,
            IPEndPoint remoteEndPoint,
            IRpcTransport transport,
            RpcCatalog catalog,
            MessageFactory messageFactory,
            MessagingOptions messagingOptions,
            InterfaceToImplementationMappingCache interfaceToImplementationMapping,
            Serializer serializer,
            RpcSerializationSessionFactory sessionFactory,
            ILogger<RpcConnection> logger)
        {
            _connectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
            _remoteEndPoint = remoteEndPoint ?? throw new ArgumentNullException(nameof(remoteEndPoint));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _messageFactory = messageFactory ?? throw new ArgumentNullException(nameof(messageFactory));
            _messagingOptions = messagingOptions ?? throw new ArgumentNullException(nameof(messagingOptions));
            _interfaceToImplementationMapping = interfaceToImplementationMapping ?? throw new ArgumentNullException(nameof(interfaceToImplementationMapping));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
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

                // For now, let's use a simpler approach - invoke the grain method directly
                // This bypasses Orleans' message pump but is simpler for RPC
                var result = await InvokeGrainMethodAsync(request);

                // Send success response
                var response = new Protocol.RpcResponse
                {
                    RequestId = request.MessageId,
                    Success = true,
                    Payload = SerializeResult(result)
                };

                _logger.LogDebug("RPC Connection: Sending success response for request {MessageId}, result type: {ResultType}, result value: {ResultValue}, payload size: {PayloadSize} bytes", 
                    request.MessageId, result?.GetType()?.FullName ?? "null", result?.ToString() ?? "null", response.Payload?.Length ?? 0);
                await SendResponseAsync(response);
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
                await _transport.SendToConnectionAsync(_connectionId, responseData, CancellationToken.None);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("No connected peer"))
            {
                // Client has disconnected - this is expected if the client lost interest in this server
                _logger.LogDebug("Client disconnected before response could be sent for request {MessageId}", response.MessageId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send response {MessageId}", response.MessageId);
                throw;
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
                _logger.LogDebug("[RPC_SERVER] No serialized arguments provided (null or empty)");
                return Array.Empty<object>();
            }

            try
            {
                // Log the raw bytes for debugging
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    var bytesHex = Convert.ToHexString(serializedArguments);
                    _logger.LogDebug("[RPC_SERVER] Raw argument bytes ({Length}): {Bytes}", serializedArguments.Length, bytesHex);
                }

                // Use isolated session for value-based deserialization
                var result = _sessionFactory.DeserializeWithIsolatedSession<object[]>(_serializer, serializedArguments);
                
                // Log deserialized arguments for debugging
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("[RPC_SERVER] Deserialized {Count} arguments", result.Length);
                    for (int i = 0; i < result.Length; i++)
                    {
                        _logger.LogDebug("[RPC_SERVER] Deserialized argument[{Index}]: Type={Type}, Value={Value}",
                            i, result[i]?.GetType()?.Name ?? "null", result[i]?.ToString() ?? "null");
                    }
                }
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RPC_SERVER] Failed to deserialize arguments. Length: {Length} bytes", serializedArguments.Length);
                throw new InvalidOperationException($"Failed to deserialize RPC arguments: {ex.Message}", ex);
            }
        }

        private async Task<object> InvokeGrainMethodAsync(Protocol.RpcRequest request)
        {
            // Get or create the grain activation
            var grainContext = await _catalog.GetOrCreateActivationAsync(request.GrainId);

            var grain = grainContext.GrainInstance;

            if (grain == null)
            {
                throw new InvalidOperationException($"Grain instance not found for {request.GrainId}");
            }

            // For a simplified RPC approach, we'll use reflection to invoke the method
            // In a production system, you'd want to use the generated invokables

            // Get the interface type - need to resolve the actual Type from GrainInterfaceType
            var grainType = grain.GetType();

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


            if (request.MethodId >= methods.Length)
            {
                throw new InvalidOperationException($"Method ID {request.MethodId} not found on interface {interfaceType.Name}. Interface has {methods.Length} methods.");
            }

            var method = methods[request.MethodId];
            
            _logger.LogInformation("[RPC_SERVER] Method mapping - MethodId: {MethodId}, Method: {MethodName}, Interface: {InterfaceType}",
                request.MethodId, method.Name, interfaceType.Name);

            // Deserialize arguments
            object[] arguments = null;
            var parameters = method.GetParameters();

            if (request.Arguments != null && request.Arguments.Length > 0)
            {
                try
                {
                    // Use the DeserializeArguments method to avoid duplication
                    arguments = DeserializeArguments(request.Arguments);
                    
                    // Log method invocation details
                    _logger.LogDebug("[RPC_SERVER] Invoking method {Method} on grain {GrainId} with {ArgCount} arguments",
                        method.Name, request.GrainId, arguments?.Length ?? 0);
                    
                    // Ensure we have the right number of arguments
                    if (arguments == null || arguments.Length != parameters.Length)
                    {
                        _logger.LogWarning("[RPC_SERVER] Argument count mismatch: expected {Expected}, got {Actual}",
                            parameters.Length, arguments?.Length ?? 0);
                        arguments = new object[parameters.Length];
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

                object result = null;
                try
                {
                    result = methodEntry.ImplementationMethod.Invoke(grain, arguments);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Exception during method.Invoke");
                    throw;
                }

                // Handle async methods
                if (result is Task task)
                {
                    await task;

                    // Check if this is a Task<T> by looking at the exact type
                    var taskType = task.GetType();
                    
                    _logger.LogDebug("Task type detected: {TaskType}, IsGenericType: {IsGeneric}, GenericTypeDefinition: {GenericDef}", 
                        taskType.FullName, 
                        taskType.IsGenericType, 
                        taskType.IsGenericType ? taskType.GetGenericTypeDefinition().FullName : "N/A");
                    
                    // If it's exactly Task (not Task<T>), return null
                    if (taskType == typeof(Task))
                    {
                        _logger.LogDebug("Detected non-generic Task, returning null to avoid VoidTaskResult serialization");
                        return null;
                    }
                    
                    // Check if the type is generic and derives from Task<T>
                    if (taskType.IsGenericType)
                    {
                        var genericDefinition = taskType.GetGenericTypeDefinition();
                        if (genericDefinition == typeof(Task<>))
                        {
                            // This is Task<T>, get the result
                            var resultProperty = taskType.GetProperty("Result");
                            if (resultProperty != null)
                            {
                                var taskResult = resultProperty.GetValue(task);
                                _logger.LogDebug("Successfully extracted Task<T> result of type {ResultType}: {Result}",
                                    taskResult?.GetType().Name ?? "null", taskResult);
                                
                                // Additional safety check: ensure we're not returning VoidTaskResult
                                if (taskResult != null && taskResult.GetType().FullName == "System.Threading.Tasks.VoidTaskResult")
                                {
                                    _logger.LogWarning("Task<T> result is VoidTaskResult, returning null instead");
                                    return null;
                                }
                                
                                return taskResult;
                            }
                        }
                    }
                    
                    // For any other Task type that's not Task<T>, return null
                    _logger.LogDebug("Non-generic or unknown Task type, returning null");
                    return null;
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
                    return taskResult;
                }

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
                _logger.LogDebug("[RPC_SERVER] SerializeResult: result is null, returning empty array");
                return Array.Empty<byte>();
            }

            var resultType = result.GetType();
            _logger.LogDebug("[RPC_SERVER] SerializeResult: attempting to serialize type {ResultType}", resultType.FullName);

            // Debug logging for VoidTaskResult issue
            if (resultType.FullName == "System.Threading.Tasks.VoidTaskResult")
            {
                _logger.LogError("CRITICAL: Attempting to serialize VoidTaskResult! This should have been caught earlier. " +
                    "Result type: {ResultType}, Result value: {ResultValue}. Stack trace: {StackTrace}", 
                    resultType.FullName, result?.ToString() ?? "null", Environment.StackTrace);
                // Return empty array instead of throwing
                return Array.Empty<byte>();
            }

            try
            {
                // Use Orleans binary serialization
                var writer = new ArrayBufferWriter<byte>();
                _serializer.Serialize(result, writer);
                var serializedBytes = writer.WrittenMemory.ToArray();
                _logger.LogDebug("[RPC_SERVER] Successfully serialized {ResultType} to {ByteCount} bytes", 
                    resultType.FullName, serializedBytes.Length);
                return serializedBytes;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RPC_SERVER] Failed to serialize result of type {ResultType}: {Error}", 
                    resultType.FullName, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Processes an IAsyncEnumerable request and starts sending results back to the client.
        /// </summary>
        public async Task ProcessAsyncEnumerableRequestAsync(Protocol.RpcAsyncEnumerableRequest request)
        {
            if (_disposed != 0)
            {
                _logger.LogWarning("Ignoring async enumerable request on disposed connection {ConnectionId}", _connectionId);
                return;
            }

            var asyncEnumerableCts = new CancellationTokenSource();
            
            try
            {
                // Store the cancellation token source for this stream
                if (!_activeAsyncEnumerables.TryAdd(request.StreamId, asyncEnumerableCts))
                {
                    _logger.LogWarning("Duplicate stream ID: {StreamId}", request.StreamId);
                    return;
                }
                
                // Start async enumeration in background
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await StreamAsyncEnumerableMethodAsync(request, asyncEnumerableCts.Token);
                    }
                    finally
                    {
                        _activeAsyncEnumerables.TryRemove(request.StreamId, out _);
                    }
                }, asyncEnumerableCts.Token);

                // Send initial response to acknowledge async enumeration started
                var response = new Protocol.RpcResponse
                {
                    RequestId = request.MessageId,
                    Success = true
                };
                await SendResponseAsync(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process async enumerable request {StreamId}", request.StreamId);
                
                // Send error item
                var errorItem = new Protocol.RpcAsyncEnumerableItem
                {
                    StreamId = request.StreamId,
                    SequenceNumber = 0,
                    IsComplete = true,
                    ErrorMessage = ex.Message
                };
                await SendAsyncEnumerableItemAsync(errorItem);
            }
        }
        
        /// <summary>
        /// Cancels an active IAsyncEnumerable stream.
        /// </summary>
        public async Task CancelAsyncEnumerableAsync(Guid streamId)
        {
            if (_activeAsyncEnumerables.TryRemove(streamId, out var cts))
            {
                cts?.Cancel();
                cts?.Dispose();
                _logger.LogDebug("Cancelled stream {StreamId}", streamId);
                
                // Send completion item
                var completeItem = new Protocol.RpcAsyncEnumerableItem
                {
                    StreamId = streamId,
                    SequenceNumber = -1,
                    IsComplete = true
                };
                await SendAsyncEnumerableItemAsync(completeItem);
            }
        }
        
        private async Task StreamAsyncEnumerableMethodAsync(Protocol.RpcAsyncEnumerableRequest request, CancellationToken cancellationToken)
        {
            try
            {
                // Get or create the grain activation
                var grainContext = await _catalog.GetOrCreateActivationAsync(request.GrainId);
                var grain = grainContext.GrainInstance;

                if (grain == null)
                {
                    throw new InvalidOperationException($"Grain instance not found for {request.GrainId}");
                }

                // Find the method to invoke
                var grainType = grain.GetType();
                Type interfaceType = null;
                
                foreach (var iface in grainType.GetInterfaces())
                {
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
                        interfaceType = iface;
                        break;
                    }
                }

                if (interfaceType == null)
                {
                    throw new InvalidOperationException($"No grain interface found on type {grainType.Name}");
                }

                // Get methods sorted alphabetically
                var methods = interfaceType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => !m.IsSpecialName)
                    .OrderBy(m => m.Name, StringComparer.Ordinal)
                    .ToArray();

                if (request.MethodId >= methods.Length)
                {
                    throw new InvalidOperationException($"Method ID {request.MethodId} not found on interface {interfaceType.Name}");
                }

                var method = methods[request.MethodId];
                
                // Verify the method returns IAsyncEnumerable<T>
                if (!method.ReturnType.IsGenericType ||
                    method.ReturnType.GetGenericTypeDefinition() != typeof(IAsyncEnumerable<>))
                {
                    throw new InvalidOperationException($"Method {method.Name} does not return IAsyncEnumerable");
                }

                // Deserialize arguments
                object[] arguments = null;
                var parameters = method.GetParameters();

                if (request.Arguments != null && request.Arguments.Length > 0)
                {
                    // Use Orleans binary deserialization
                    var deserializedArgs = _serializer.Deserialize<object[]>(request.Arguments);
                    
                    arguments = new object[parameters.Length];
                    
                    for (int i = 0; i < Math.Min(deserializedArgs?.Length ?? 0, parameters.Length); i++)
                    {
                        var paramType = parameters[i].ParameterType;
                        if (paramType == typeof(CancellationToken))
                        {
                            arguments[i] = cancellationToken;
                        }
                        else
                        {
                            arguments[i] = deserializedArgs[i];
                        }
                    }
                }
                else
                {
                    arguments = new object[parameters.Length];
                    // Check if last parameter is CancellationToken
                    if (parameters.Length > 0 && parameters[^1].ParameterType == typeof(CancellationToken))
                    {
                        arguments[^1] = cancellationToken;
                    }
                }

                // Find the implementation method
                var implementationMap = _interfaceToImplementationMapping.GetOrCreate(grainType, interfaceType);
                if (!implementationMap.TryGetValue(method, out var methodEntry))
                {
                    throw new InvalidOperationException($"Implementation not found for method {method.Name}");
                }

                // Invoke the method
                var result = methodEntry.ImplementationMethod.Invoke(grain, arguments);
                
                // Get the IAsyncEnumerable<T>
                var itemType = method.ReturnType.GetGenericArguments()[0];
                var enumerableType = typeof(IAsyncEnumerable<>).MakeGenericType(itemType);
                
                // Use reflection to iterate the async enumerable
                var getAsyncEnumeratorMethod = enumerableType.GetMethod("GetAsyncEnumerator");
                var asyncEnumerator = getAsyncEnumeratorMethod.Invoke(result, new object[] { cancellationToken });
                var moveNextAsyncMethod = asyncEnumerator.GetType().GetMethod("MoveNextAsync");
                var currentProperty = asyncEnumerator.GetType().GetProperty("Current");
                
                long sequenceNumber = 0;
                
                while (!cancellationToken.IsCancellationRequested)
                {
                    var moveNextTask = (ValueTask<bool>)moveNextAsyncMethod.Invoke(asyncEnumerator, null);
                    var hasNext = await moveNextTask;
                    
                    if (!hasNext)
                        break;
                    
                    var current = currentProperty.GetValue(asyncEnumerator);
                    
                    // Serialize and send the item
                    var item = new Protocol.RpcAsyncEnumerableItem
                    {
                        StreamId = request.StreamId,
                        SequenceNumber = sequenceNumber++,
                        ItemData = SerializeResult(current),
                        IsComplete = false
                    };
                    
                    await SendAsyncEnumerableItemAsync(item);
                }
                
                // Send completion item
                var completeItem = new Protocol.RpcAsyncEnumerableItem
                {
                    StreamId = request.StreamId,
                    SequenceNumber = sequenceNumber,
                    IsComplete = true
                };
                
                await SendAsyncEnumerableItemAsync(completeItem);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enumerating from grain method");
                
                // Send error item
                var errorItem = new Protocol.RpcAsyncEnumerableItem
                {
                    StreamId = request.StreamId,
                    SequenceNumber = -1,
                    IsComplete = true,
                    ErrorMessage = ex.Message
                };
                
                await SendAsyncEnumerableItemAsync(errorItem);
            }
        }
        
        private async Task SendAsyncEnumerableItemAsync(Protocol.RpcAsyncEnumerableItem item)
        {
            var messageSerializer = _catalog.ServiceProvider.GetRequiredService<Protocol.RpcMessageSerializer>();
            var messageData = messageSerializer.SerializeMessage(item);
            
            // Use SendToConnectionAsync when we have a connection ID
            if (!string.IsNullOrEmpty(_connectionId))
            {
                await _transport.SendToConnectionAsync(_connectionId, messageData, CancellationToken.None);
            }
            else
            {
                await _transport.SendAsync(_remoteEndPoint, messageData, CancellationToken.None);
            }
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
            {
                // Cancel all active async enumerable operations
                foreach (var kvp in _activeAsyncEnumerables)
                {
                    kvp.Value?.Cancel();
                    kvp.Value?.Dispose();
                }
                _activeAsyncEnumerables.Clear();
                
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
            }
        }
    }

}
