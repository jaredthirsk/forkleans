// Copyright (c) Granville. All rights reserved.
// Licensed under the MIT License.

using System;

namespace Granville.Rpc.Security;

/// <summary>
/// Restricts access to server-to-server calls only.
/// Clients (User, Guest roles) cannot call methods with this attribute.
/// </summary>
/// <remarks>
/// Equivalent to <c>[RequireRole(UserRole.Server)]</c> but more explicit
/// about intent. Use for internal infrastructure grains.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Interface | AttributeTargets.Class,
    Inherited = true,
    AllowMultiple = false)]
public sealed class ServerOnlyAttribute : Attribute
{
}
