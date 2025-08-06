using System;
using System.Buffers;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.CodeGeneration;
using Orleans.Configuration;
using Orleans.GrainReferences;
using Orleans.Messaging;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Invocation;
using Orleans.Serialization.Session;

namespace Granville.Rpc
{
    /// <summary>
    /// RPC-specific implementation of IRuntimeClient for outside (client) use.
    /// </summary>
    internal sealed class OutsideRpcRuntimeClient : IRuntimeClient
    {
        private readonly ILogger<OutsideRpcRuntimeClient> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly TimeProvider _timeProvider;
        private readonly ILocalClientDetails _clientDetails;
        private readonly OutsideRpcClient _rpcClient;
        private readonly Serializer _serializer;
        private readonly RpcSerializationSessionFactory _sessionFactory;
        private TimeSpan _responseTimeout = TimeSpan.FromSeconds(30);
        private IInternalGrainFactory _internalGrainFactory;

        public OutsideRpcRuntimeClient(
            IServiceProvider serviceProvider,
            ILogger<OutsideRpcRuntimeClient> logger,
            TimeProvider timeProvider,
            ILocalClientDetails clientDetails,
            OutsideRpcClient rpcClient,
            RpcSerializationSessionFactory sessionFactory)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            _clientDetails = clientDetails ?? throw new ArgumentNullException(nameof(clientDetails));
            _rpcClient = rpcClient ?? throw new ArgumentNullException(nameof(rpcClient));
            _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
            _serializer = serviceProvider.GetRequiredService<Serializer>();
        }

        public TimeProvider TimeProvider => _timeProvider;

        public IInternalGrainFactory InternalGrainFactory => _internalGrainFactory ??= _serviceProvider.GetRequiredService<IGrainFactory>() as IInternalGrainFactory;

        public string CurrentActivationIdentity => _clientDetails.ClientId;

        public IServiceProvider ServiceProvider => _serviceProvider;

        private IGrainReferenceRuntime _grainReferenceRuntime;
        public IGrainReferenceRuntime GrainReferenceRuntime 
        { 
            get 
            {
                if (_grainReferenceRuntime == null)
                {
                    _grainReferenceRuntime = _serviceProvider.GetRequiredService<IGrainReferenceRuntime>();
                }
                return _grainReferenceRuntime;
            }
            private set
            {
                _grainReferenceRuntime = value;
            }
        }

        public TimeSpan GetResponseTimeout() => _responseTimeout;

        public void SetResponseTimeout(TimeSpan timeout) => _responseTimeout = timeout;

        /// <summary>
        /// Consume services from the service provider to break circular dependencies.
        /// This must be called after the DI container is fully built.
        /// </summary>
        public void ConsumeServices()
        {
            // Force initialization of the lazy property
            _ = this.GrainReferenceRuntime;
        }

        public void SendRequest(GrainReference target, IInvokable request, IResponseCompletionSource context, InvokeMethodOptions options)
        {
            try
            {
                _logger.LogTrace("SendRequest called for {Target} method {MethodName} (entry point)", target, request.GetMethodName());
            
            Task.Run(async () =>
            {
                try
                {
                    // Create RPC request
                    var methodId = GetMethodId(target.InterfaceType, request.GetMethodName());
                    
                    // Get return type information
                    string returnTypeName = null;
                    var method = request.GetMethod();
                    if (method != null)
                    {
                        var returnType = method.ReturnType;
                        _logger.LogDebug("Method {MethodName} return type: {ReturnType}", request.GetMethodName(), returnType);
                        
                        if (returnType.IsGenericType)
                        {
                            // Handle Task<T> or ValueTask<T>
                            var genericDef = returnType.GetGenericTypeDefinition();
                            if (genericDef == typeof(Task<>) || genericDef == typeof(ValueTask<>))
                            {
                                var actualReturnType = returnType.GetGenericArguments()[0];
                                returnTypeName = actualReturnType.AssemblyQualifiedName;
                                _logger.LogDebug("Extracted actual return type: {ActualReturnType}", actualReturnType);
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Could not get method info for {MethodName}", request.GetMethodName());
                    }
                    
                    var rpcRequest = new Protocol.RpcRequest
                    {
                        MessageId = Guid.NewGuid(),
                        GrainId = target.GrainId,
                        InterfaceType = target.InterfaceType,
                        MethodId = methodId,
                        Arguments = SerializeArguments(request),
                        TimeoutMs = 30000, // 30 seconds timeout
                        ReturnTypeName = returnTypeName
                    };
                    
                    _logger.LogDebug("OutsideRpcRuntimeClient: Sending RPC request {MessageId} to grain {GrainId} interface {InterfaceType} method {MethodId}, timeout: {TimeoutMs}ms", 
                        rpcRequest.MessageId, rpcRequest.GrainId, rpcRequest.InterfaceType, rpcRequest.MethodId, rpcRequest.TimeoutMs);
                    
                    // Send request and wait for response
                    var response = await _rpcClient.SendRequestAsync(rpcRequest);
                    
                    _logger.LogDebug("OutsideRpcRuntimeClient: Received RPC response for {MessageId}: Success={Success}, PayloadSize={PayloadSize}", 
                        rpcRequest.MessageId, response.Success, response.Payload?.Length ?? 0);
                    
                    if (response.Success)
                    {
                        // Deserialize the result
                        object result = null;
                        if (response.Payload != null && response.Payload.Length > 0)
                        {
                            _logger.LogTrace("Deserializing response payload (length: {Length} bytes)", response.Payload.Length);
                            
                            // Use Orleans binary deserialization
                            // Try to deserialize using the return type information if available
                            if (!string.IsNullOrEmpty(rpcRequest.ReturnTypeName))
                            {
                                try
                                {
                                    var returnType = Type.GetType(rpcRequest.ReturnTypeName);
                                    if (returnType != null)
                                    {
                                        result = DeserializePayload(response.Payload, returnType);
                                    }
                                    else
                                    {
                                        _logger.LogWarning("Could not load return type {ReturnTypeName}", rpcRequest.ReturnTypeName);
                                        result = _sessionFactory.DeserializeWithIsolatedSession<object>(_serializer, new ReadOnlyMemory<byte>(response.Payload));
                                        
                                        // Handle JsonElement conversion for primitive types
                                        if (result is System.Text.Json.JsonElement element)
                                        {
                                            result = element.ValueKind switch
                                            {
                                                System.Text.Json.JsonValueKind.True => true,
                                                System.Text.Json.JsonValueKind.False => false,
                                                System.Text.Json.JsonValueKind.Number => element.TryGetInt32(out var intValue) ? intValue : element.GetDouble(),
                                                System.Text.Json.JsonValueKind.String => element.GetString(),
                                                System.Text.Json.JsonValueKind.Null => null,
                                                _ => result // Keep as JsonElement for complex types
                                            };
                                            
                                            _logger.LogDebug("Converted JsonElement of kind {Kind} to {Type}: {Value} (returnType null)", 
                                                element.ValueKind, result?.GetType().Name ?? "null", result);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Error deserializing result of type {ReturnTypeName}", rpcRequest.ReturnTypeName);
                                    // Fall back to object deserialization
                                    result = _sessionFactory.DeserializeWithIsolatedSession<object>(_serializer, new ReadOnlyMemory<byte>(response.Payload));
                                }
                            }
                            else
                            {
                                // No type information available, deserialize as object
                                result = _sessionFactory.DeserializeWithIsolatedSession<object>(_serializer, new ReadOnlyMemory<byte>(response.Payload));
                            }
                        }
                        
                        _logger.LogTrace("Final deserialized result - Type: {Type}, Value: {Value}", 
                            result?.GetType().Name ?? "null", result);
                        
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SendRequest method entry");
                context.Complete(Response.FromException(ex));
            }
        }
        
        private byte[] SerializeArguments(IInvokable request)
        {
            var argCount = request.GetArgumentCount();
            if (argCount == 0)
            {
                _logger.LogDebug("[RPC_CLIENT] SerializeArguments: No arguments to serialize");
                return Array.Empty<byte>();
            }
            
            var args = new object[argCount];
            var invokableType = request.GetType();
            
            for (int i = 0; i < argCount; i++)
            {
                args[i] = request.GetArgument(i);
                
                // If GetArgument returns null, try to get the value via reflection
                // This is a workaround for Orleans-generated proxies that don't properly implement GetArgument
                if (args[i] == null && argCount > 0)
                {
                    _logger.LogWarning("[RPC_CLIENT] GetArgument({Index}) returned null, using reflection fallback", i);
                    var fieldName = $"arg{i}";
                    var field = invokableType.GetField(fieldName, 
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    
                    if (field != null)
                    {
                        args[i] = field.GetValue(request);
                        _logger.LogWarning("[RPC_CLIENT] Reflection found field {FieldName} = {Value} (Type: {Type})", 
                            fieldName, 
                            args[i]?.ToString() ?? "null",
                            args[i]?.GetType()?.FullName ?? "null");
                    }
                    else
                    {
                        _logger.LogError("[RPC_CLIENT] Field {FieldName} not found on type {Type}", 
                            fieldName, invokableType.FullName);
                    }
                }
                
                _logger.LogDebug("[RPC_CLIENT] SerializeArguments: arg[{Index}] = {Type}: {Value}", 
                    i, args[i]?.GetType()?.Name ?? "null", args[i]?.ToString() ?? "null");
            }
            
            // Use isolated session for value-based serialization
            var result = _sessionFactory.SerializeArgumentsWithIsolatedSession(_serializer, args);
            
            _logger.LogDebug("[RPC_CLIENT] SerializeArguments: Serialized {ArgCount} arguments to {ByteCount} bytes", 
                argCount, result.Length);
            
            // Log the raw bytes for debugging
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                var bytesHex = Convert.ToHexString(result);
                _logger.LogDebug("[RPC_CLIENT] Serialized argument bytes ({Length}): {Bytes}", result.Length, bytesHex);
            }
            
            return result;
        }
        
        private int GetMethodId(GrainInterfaceType interfaceType, string methodName)
        {
            // Find the actual interface type
            // This is a simplified approach - in production, you'd want to cache this
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            Type interfaceClrType = null;
            
            // GrainInterfaceType.Value is an IdSpan, convert to string
            var typeName = interfaceType.Value.ToString();
            
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
            
            
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name == methodName)
                {
                    _logger.LogTrace("Method {MethodName} on {Interface} has ID {MethodId}", methodName, interfaceType, i);
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
        
        private object DeserializePayload(byte[] payload, Type returnType)
        {
            if (payload == null || payload.Length == 0)
            {
                // Handle void/null returns
                if (returnType == typeof(void) || returnType == typeof(Task))
                {
                    return null;
                }
                
                // For empty payload, return default value
                return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
            }

            try
            {
                // Check for marker byte and remove it
                if (payload.Length > 0 && (payload[0] == 0x00 || payload[0] == 0xFE || payload[0] == 0xFF))
                {
                    _logger.LogDebug("Removing marker byte {MarkerByte:X2} from payload", payload[0]);
                    // Skip the marker byte
                    var dataWithoutMarker = new byte[payload.Length - 1];
                    Array.Copy(payload, 1, dataWithoutMarker, 0, dataWithoutMarker.Length);
                    payload = dataWithoutMarker;
                }

                // Use the session factory to deserialize with isolated session
                var deserializeMethod = typeof(RpcSerializationSessionFactory)
                    .GetMethod("DeserializeWithIsolatedSession", BindingFlags.Public | BindingFlags.Instance)
                    ?.MakeGenericMethod(returnType);
                    
                if (deserializeMethod == null)
                {
                    throw new InvalidOperationException("Could not find DeserializeWithIsolatedSession method on RpcSerializationSessionFactory");
                }
                
                var memory = new ReadOnlyMemory<byte>(payload);
                
                // Invoke with proper parameters: instance, serializer, memory
                var result = deserializeMethod.Invoke(_sessionFactory, new object[] { _serializer, memory });
                
                _logger.LogDebug("Successfully deserialized payload of type {ReturnType}", returnType.FullName);
                
                return result;
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                // Unwrap the inner exception from reflection
                _logger.LogError(tie.InnerException, "Failed to deserialize payload of type {ReturnType}. Payload length: {Length} bytes", 
                    returnType.FullName, payload.Length);
                throw new InvalidOperationException($"Failed to deserialize response of type {returnType.FullName}", tie.InnerException);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize payload of type {ReturnType}. Payload length: {Length} bytes", 
                    returnType.FullName, payload.Length);
                throw new InvalidOperationException($"Failed to deserialize response of type {returnType.FullName}", ex);
            }
        }
    }
}