// Copyright (c) Granville. All rights reserved.
// Licensed under the MIT License.

using System;

namespace Granville.Rpc.Security;

/// <summary>
/// Marks a grain interface or method as accessible by clients.
/// Grains/methods without this attribute can only be accessed by servers
/// when strict mode is enabled.
/// </summary>
/// <remarks>
/// This is a safety measure to prevent clients from accessing
/// internal infrastructure grains. Only effective when
/// <c>RpcSecurityOptions.EnforceClientAccessibleAttribute</c> is <c>true</c>.
/// When applied to an interface, all methods are client-accessible.
/// When applied to a method, only that method is client-accessible.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Interface | AttributeTargets.Method,
    Inherited = true,
    AllowMultiple = false)]
public sealed class ClientAccessibleAttribute : Attribute
{
}
