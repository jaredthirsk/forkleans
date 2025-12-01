// Copyright (c) Granville. All rights reserved.
// Licensed under the MIT License.

using System;

namespace Granville.Rpc.Security;

/// <summary>
/// Requires the caller to have at least the specified role.
/// Multiple <see cref="RequireRoleAttribute"/> attributes on a method are OR'd together.
/// </summary>
/// <remarks>
/// Role comparison uses >= semantics, so <c>[RequireRole(UserRole.User)]</c> allows
/// User, Server, and Admin roles.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Interface | AttributeTargets.Class,
    Inherited = true,
    AllowMultiple = true)]
public sealed class RequireRoleAttribute : Attribute
{
    /// <summary>
    /// The minimum required role.
    /// </summary>
    public UserRole Role { get; }

    /// <summary>
    /// Creates a new <see cref="RequireRoleAttribute"/>.
    /// </summary>
    /// <param name="role">The minimum required role.</param>
    public RequireRoleAttribute(UserRole role)
    {
        Role = role;
    }
}
