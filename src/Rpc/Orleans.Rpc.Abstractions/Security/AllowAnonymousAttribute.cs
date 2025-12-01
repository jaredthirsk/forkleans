// Copyright (c) Granville. All rights reserved.
// Licensed under the MIT License.

using System;

namespace Granville.Rpc.Security;

/// <summary>
/// Allows anonymous (unauthenticated) access to a method.
/// Overrides <see cref="AuthorizeAttribute"/> on the containing interface or class.
/// </summary>
/// <remarks>
/// Use sparingly - only for methods that genuinely need public access
/// like server info endpoints.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Method,
    Inherited = true,
    AllowMultiple = false)]
public sealed class AllowAnonymousAttribute : Attribute
{
}
