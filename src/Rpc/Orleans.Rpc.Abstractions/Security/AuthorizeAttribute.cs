// Copyright (c) Granville. All rights reserved.
// Licensed under the MIT License.

using System;

namespace Granville.Rpc.Security;

/// <summary>
/// Requires the caller to be authenticated (have a valid PSK session).
/// Can be applied to methods, interfaces, or classes.
/// </summary>
/// <remarks>
/// When applied to an interface or class, all methods require authentication
/// unless overridden with <see cref="AllowAnonymousAttribute"/>.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Interface | AttributeTargets.Class,
    Inherited = true,
    AllowMultiple = false)]
public sealed class AuthorizeAttribute : Attribute
{
}
