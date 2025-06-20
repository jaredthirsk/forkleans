using Forkleans.CodeGenerator.SyntaxGeneration;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using Forkleans.CodeGenerator.Diagnostics;
using System.Linq;
using System.Diagnostics;

namespace Forkleans.CodeGenerator
{
    [DebuggerDisplay("{InterfaceType} (proxy base {ProxyBaseType})")]
    internal class ProxyInterfaceDescription : IEquatable<ProxyInterfaceDescription>
    {
        private static readonly char[] FilteredNameChars = new char[] { '`', '.' };
        private List<ProxyMethodDescription> _methods;

        public ProxyInterfaceDescription(
            CodeGenerator codeGenerator,
            INamedTypeSymbol proxyBaseType,
            INamedTypeSymbol interfaceType)
        {
            ValidateBaseClass(codeGenerator.LibraryTypes, proxyBaseType);

            var prop = interfaceType.GetAllMembers<IPropertySymbol>().FirstOrDefault();
            if (prop is { })
            {
                throw new OrleansGeneratorDiagnosticAnalysisException(RpcInterfacePropertyDiagnostic.CreateDiagnostic(interfaceType, prop));
            }

            CodeGenerator = codeGenerator;
            InterfaceType = interfaceType;
            Name = codeGenerator.GetAlias(interfaceType) ?? interfaceType.Name;
            ProxyBaseType = proxyBaseType;

            // If the name is a user-defined name which specified a generic arity, strip the arity backtick now
            if (Name.IndexOfAny(FilteredNameChars) >= 0)
            {
                foreach (var c in FilteredNameChars)
                {
                    Name = Name.Replace(c, '_');
                }
            }

            GeneratedNamespace = InterfaceType.GetNamespaceAndNesting() switch
            {
                { Length: > 0 } ns => $"{CodeGenerator.CodeGeneratorName}.{ns}",
                _ => CodeGenerator.CodeGeneratorName
            };

            var names = new HashSet<string>(StringComparer.Ordinal);
            TypeParameters = new List<(string Name, ITypeParameterSymbol Parameter)>();

            foreach (var tp in interfaceType.GetAllTypeParameters())
            {
                var tpName = GetTypeParameterName(names, tp);
                TypeParameters.Add((tpName, tp));
            }

            static string GetTypeParameterName(HashSet<string> names, ITypeParameterSymbol tp)
            {
                var count = 0;
                var result = tp.Name;
                while (names.Contains(result))
                {
                    result = $"{tp.Name}_{++count}";
                }

                names.Add(result);
                return result;
            }
        }

        public CodeGenerator CodeGenerator { get; }

        private List<ProxyMethodDescription> GetMethods()
        {
            var result = new List<ProxyMethodDescription>();
            foreach (var iface in GetAllInterfaces(InterfaceType))
            {
                foreach (var method in iface.GetDeclaredInstanceMembers<IMethodSymbol>())
                {
                    if (method.MethodKind == MethodKind.ExplicitInterfaceImplementation)
                    {
                        // Explicit implementations can be ignored when generating a proxy.
                        // Proxies must implement every method explicitly to ensure faithful reproduction of the interface behavior.
                        // At the calling side, the explicit implementation will be called if it was not overridden by a derived type.
                        continue;
                    }

                    var methodDescription = CodeGenerator.GetProxyMethodDescription(InterfaceType, method: method);
                    result.Add(methodDescription);
                }
            }

            return result;

            static IEnumerable<INamedTypeSymbol> GetAllInterfaces(INamedTypeSymbol s)
            {
                if (s.TypeKind == TypeKind.Interface)
                {
                    yield return s;
                }

                foreach (var i in s.AllInterfaces)

                {
                    yield return i;
                }
            }
        }

        public string Name { get; }
        public INamedTypeSymbol InterfaceType { get; }
        public List<ProxyMethodDescription> Methods => _methods ??= GetMethods(); 
        public SemanticModel SemanticModel { get; }
        public string GeneratedNamespace { get; }
        public List<(string Name, ITypeParameterSymbol Parameter)> TypeParameters { get; }
        public INamedTypeSymbol ProxyBaseType { get; }

        private static void ValidateBaseClass(LibraryTypes l, INamedTypeSymbol baseClass)
        {
            ValidateGenericInvokeAsync(l, baseClass);
            ValidateNonGenericInvokeAsync(l, baseClass);

            static void ValidateGenericInvokeAsync(LibraryTypes l, INamedTypeSymbol baseClass)
            {
                var found = false;
                string complaint = null;
                ISymbol complaintMember = null;
                foreach (var member in baseClass.GetMembers("InvokeAsync"))
                {
                    if (member is not IMethodSymbol method)
                    {
                        complaintMember = member;
                        complaint = "not a method";
                        continue;
                    }

                    if (method.TypeParameters.Length != 1)
                    {
                        complaintMember = member;
                        complaint = "incorrect number of type parameters (expected one type parameter)";
                        continue;
                    }

                    if (method.Parameters.Length != 1)
                    {
                        complaintMember = member;
                        complaint = $"missing parameter (expected a parameter of type {l.IInvokable.ToDisplayString()})";
                        continue;
                    }

                    var paramType = method.Parameters[0].Type;
                    if (!SymbolEqualityComparer.Default.Equals(paramType, l.IInvokable))
                    {
                        var implementsIInvokable = false;
                        foreach (var @interface in paramType.AllInterfaces)
                        {
                            if (SymbolEqualityComparer.Default.Equals(@interface, l.IInvokable))
                            {
                                implementsIInvokable = true;
                                break;
                            }
                        }

                        if (!implementsIInvokable)
                        {
                            complaintMember = member;
                            complaint = $"incorrect parameter type (found {paramType}, expected {l.IInvokable} or a type which implements {l.IInvokable})";
                            continue;
                        }
                    }

                    var expectedReturnType = l.ValueTask_1.Construct(method.TypeParameters[0]);
                    if (!SymbolEqualityComparer.Default.Equals(method.ReturnType, expectedReturnType))
                    {
                        complaintMember = member;
                        complaint = $"incorrect return type (found: {method.ReturnType.ToDisplayString()}, expected {expectedReturnType.ToDisplayString()})";
                        continue;
                    }

                    found = true;
                }

                if (!found)
                {
                    var notFoundMessage = $"Proxy base class {baseClass} does not contain a definition for ValueTask<T> InvokeAsync<T>(IInvokable)";
                    var locationMember = complaintMember ?? baseClass;
                    var complaintMessage = complaint switch
                    {
                        { Length: > 0 } => $"{notFoundMessage}. Complaint: {complaint} for symbol: {complaintMember.ToDisplayString()}",
                        _ => notFoundMessage,
                    };
                    var diagnostic = IncorrectProxyBaseClassSpecificationDiagnostic.CreateDiagnostic(baseClass, locationMember.Locations.First(), complaintMessage);
                    throw new OrleansGeneratorDiagnosticAnalysisException(diagnostic);
                }
            }
            
            static void ValidateNonGenericInvokeAsync(LibraryTypes l, INamedTypeSymbol baseClass)
            {
                var found = false;
                string complaint = null;
                ISymbol complaintMember = null;
                foreach (var member in baseClass.GetMembers("InvokeAsync"))
                {
                    if (member is not IMethodSymbol method)
                    {
                        complaintMember = member;
                        complaint = "not a method";
                        continue;
                    }

                    if (method.TypeParameters.Length != 0)
                    {
                        complaintMember = member;
                        complaint = "incorrect number of type parameters (expected zero)";
                        continue;
                    }

                    if (method.Parameters.Length != 1)
                    {
                        complaintMember = member;
                        complaint = $"missing parameter (expected a parameter of type {l.IInvokable.ToDisplayString()})";
                        continue;
                    }

                    var paramType = method.Parameters[0].Type;
                    if (!SymbolEqualityComparer.Default.Equals(paramType, l.IInvokable))
                    {
                        var implementsIInvokable = false;
                        foreach (var @interface in paramType.AllInterfaces)
                        {
                            if (SymbolEqualityComparer.Default.Equals(@interface, l.IInvokable))
                            {
                                implementsIInvokable = true;
                                break;
                            }
                        }

                        if (!implementsIInvokable)
                        {
                            complaintMember = member;
                            complaint = $"incorrect parameter type (found {method.Parameters[0].Type}, expected {l.IInvokable})";
                            continue;
                        }
                    }

                    if (!SymbolEqualityComparer.Default.Equals(method.ReturnType, l.ValueTask))
                    {
                        complaintMember = member;
                        complaint = $"incorrect return type (found: {method.ReturnType.ToDisplayString()}, expected {l.ValueTask.ToDisplayString()})";
                        continue;
                    }

                    found = true;
                }

                if (!found)
                {
                    var notFoundMessage = $"Proxy base class {baseClass} does not contain a definition for ValueTask InvokeAsync(IInvokable)";
                    var locationMember = complaintMember ?? baseClass;
                    var complaintMessage = complaint switch
                    {
                        { Length: > 0 } => $"{notFoundMessage}. Complaint: {complaint} for symbol: {complaintMember.ToDisplayString()}",
                        _ => notFoundMessage,
                    };
                    var diagnostic = IncorrectProxyBaseClassSpecificationDiagnostic.CreateDiagnostic(baseClass, locationMember.Locations.First(), complaintMessage);
                    throw new OrleansGeneratorDiagnosticAnalysisException(diagnostic);
                }
            }
        }

        public bool Equals(ProxyInterfaceDescription other) => SymbolEqualityComparer.Default.Equals(InterfaceType, other.InterfaceType) && SymbolEqualityComparer.Default.Equals(ProxyBaseType, other.ProxyBaseType);
        public override bool Equals(object obj) => obj is ProxyInterfaceDescription other && Equals(other);
        public override int GetHashCode() => SymbolEqualityComparer.Default.GetHashCode(InterfaceType) * 17 ^ SymbolEqualityComparer.Default.GetHashCode(ProxyBaseType);
        public override string ToString() => $"Type: {InterfaceType}, ProxyBaseType: {ProxyBaseType}";
    }
}