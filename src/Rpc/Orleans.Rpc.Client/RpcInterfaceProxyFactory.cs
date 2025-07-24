using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Orleans;
using Orleans.Runtime;
using Orleans.GrainReferences;
using Orleans.Serialization.Invocation;

namespace Granville.Rpc
{
    /// <summary>
    /// Creates proxy classes for RPC grain interfaces that wrap RpcGrainReference.
    /// </summary>
    internal static class RpcInterfaceProxyFactory
    {
        private static readonly ConcurrentDictionary<Type, Type> _proxyTypeCache = new();
        private static readonly AssemblyName _assemblyName = new("RpcInterfaceProxies");
        private static readonly AssemblyBuilder _assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(_assemblyName, AssemblyBuilderAccess.Run);
        private static readonly ModuleBuilder _moduleBuilder = _assemblyBuilder.DefineDynamicModule("RpcInterfaceProxies");

        /// <summary>
        /// Creates a proxy type that implements the specified grain interface and wraps an RpcGrainReference.
        /// </summary>
        public static Type GetOrCreateProxyType(Type grainInterfaceType)
        {
            if (!grainInterfaceType.IsInterface)
                throw new ArgumentException($"Type {grainInterfaceType} must be an interface", nameof(grainInterfaceType));

            return _proxyTypeCache.GetOrAdd(grainInterfaceType, CreateProxyType);
        }

        private static Type CreateProxyType(Type interfaceType)
        {
            var proxyTypeName = $"RpcProxy_{interfaceType.Name}_{Guid.NewGuid():N}";
            var typeBuilder = _moduleBuilder.DefineType(
                proxyTypeName,
                TypeAttributes.Public | TypeAttributes.Class,
                typeof(object),
                new[] { interfaceType });

            // Add field to hold the RpcGrainReference
            var grainRefField = typeBuilder.DefineField("_grainReference", typeof(RpcGrainReference), FieldAttributes.Private);

            // Add constructor
            var ctorBuilder = typeBuilder.DefineConstructor(
                MethodAttributes.Public,
                CallingConventions.Standard,
                new[] { typeof(RpcGrainReference) });

            var ctorIL = ctorBuilder.GetILGenerator();
            ctorIL.Emit(OpCodes.Ldarg_0); // this
            ctorIL.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)); // base()
            ctorIL.Emit(OpCodes.Ldarg_0); // this
            ctorIL.Emit(OpCodes.Ldarg_1); // grainReference
            ctorIL.Emit(OpCodes.Stfld, grainRefField); // _grainReference = grainReference
            ctorIL.Emit(OpCodes.Ret);

            // Get the InvokeRpcMethodAsync method from RpcGrainReference
            var invokeRpcMethod = typeof(RpcGrainReference).GetMethod("InvokeRpcMethodAsync", BindingFlags.Public | BindingFlags.Instance);

            // Implement interface methods
            foreach (var method in interfaceType.GetMethods())
            {
                if (method.IsSpecialName) continue; // Skip property getters/setters

                var parameters = method.GetParameters();
                var parameterTypes = parameters.Select(p => p.ParameterType).ToArray();

                var methodBuilder = typeBuilder.DefineMethod(
                    method.Name,
                    MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final,
                    method.ReturnType,
                    parameterTypes);

                var methodIL = methodBuilder.GetILGenerator();

                // Create array for arguments
                var argsLocal = methodIL.DeclareLocal(typeof(object[]));
                methodIL.Emit(OpCodes.Ldc_I4, parameters.Length);
                methodIL.Emit(OpCodes.Newarr, typeof(object));
                methodIL.Emit(OpCodes.Stloc, argsLocal);

                // Pack arguments into array
                for (int i = 0; i < parameters.Length; i++)
                {
                    methodIL.Emit(OpCodes.Ldloc, argsLocal);
                    methodIL.Emit(OpCodes.Ldc_I4, i);
                    methodIL.Emit(OpCodes.Ldarg, i + 1);
                    if (parameterTypes[i].IsValueType)
                    {
                        methodIL.Emit(OpCodes.Box, parameterTypes[i]);
                    }
                    methodIL.Emit(OpCodes.Stelem_Ref);
                }

                // Get method ID (use method name hash for simplicity)
                methodIL.Emit(OpCodes.Ldarg_0); // this
                methodIL.Emit(OpCodes.Ldfld, grainRefField); // _grainReference
                methodIL.Emit(OpCodes.Ldc_I4, method.Name.GetHashCode()); // methodId
                methodIL.Emit(OpCodes.Ldloc, argsLocal); // arguments

                // Call InvokeRpcMethodAsync<T>
                if (method.ReturnType == typeof(Task))
                {
                    // For Task return type, use object as T and discard result
                    var genericInvoke = invokeRpcMethod.MakeGenericMethod(typeof(object));
                    methodIL.Emit(OpCodes.Call, genericInvoke);
                    // Convert Task<object> to Task
                    var continueWith = typeof(Task<object>).GetMethod("ContinueWith", new[] { typeof(Action<Task<object>>) });
                    methodIL.Emit(OpCodes.Ldnull);
                    methodIL.Emit(OpCodes.Ldftn, typeof(RpcInterfaceProxyFactory).GetMethod(nameof(DiscardResult), BindingFlags.Static | BindingFlags.NonPublic));
                    methodIL.Emit(OpCodes.Newobj, typeof(Action<Task<object>>).GetConstructor(new[] { typeof(object), typeof(IntPtr) }));
                    methodIL.Emit(OpCodes.Callvirt, continueWith);
                }
                else if (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    // For Task<T> return type
                    var resultType = method.ReturnType.GetGenericArguments()[0];
                    var genericInvoke = invokeRpcMethod.MakeGenericMethod(resultType);
                    methodIL.Emit(OpCodes.Call, genericInvoke);
                }
                else if (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition().FullName == "System.Collections.Generic.IAsyncEnumerable`1")
                {
                    // For IAsyncEnumerable<T> - not supported yet, throw
                    methodIL.Emit(OpCodes.Ldstr, "IAsyncEnumerable methods not yet supported in RPC proxy");
                    methodIL.Emit(OpCodes.Newobj, typeof(NotSupportedException).GetConstructor(new[] { typeof(string) }));
                    methodIL.Emit(OpCodes.Throw);
                }
                else
                {
                    throw new NotSupportedException($"Method {method.Name} has unsupported return type {method.ReturnType}");
                }

                methodIL.Emit(OpCodes.Ret);

                // Override the interface method
                typeBuilder.DefineMethodOverride(methodBuilder, method);
            }

            return typeBuilder.CreateType();
        }

        private static void DiscardResult(Task<object> task)
        {
            // Helper method to convert Task<object> to Task
        }
    }
}