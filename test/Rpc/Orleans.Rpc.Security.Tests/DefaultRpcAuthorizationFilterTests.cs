// Copyright (c) Granville. All rights reserved.
// Licensed under the MIT License.

using Granville.Rpc.Security;
using Granville.Rpc.Security.Authorization;
using Granville.Rpc.Security.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orleans.Runtime;
using System.Net;
using System.Reflection;
using Xunit;

namespace Orleans.Rpc.Security.Tests;

public class DefaultRpcAuthorizationFilterTests
{
    private readonly ILogger<DefaultRpcAuthorizationFilter> _logger;

    public DefaultRpcAuthorizationFilterTests()
    {
        _logger = NullLogger<DefaultRpcAuthorizationFilter>.Instance;
    }

    private DefaultRpcAuthorizationFilter CreateFilter(RpcSecurityOptions? options = null)
    {
        var opts = options ?? new RpcSecurityOptions();
        return new DefaultRpcAuthorizationFilter(
            _logger,
            Options.Create(opts));
    }

    private RpcAuthorizationContext CreateContext(
        Type interfaceType,
        string methodName,
        RpcUserIdentity? user = null)
    {
        var method = interfaceType.GetMethod(methodName)
            ?? throw new InvalidOperationException($"Method {methodName} not found on {interfaceType.Name}");

        return new RpcAuthorizationContext
        {
            GrainInterface = interfaceType,
            Method = method,
            GrainId = GrainId.Create("test", "test-id"),
            User = user,
            RemoteEndpoint = new IPEndPoint(IPAddress.Loopback, 12345),
            ConnectionId = "test-connection",
            RequestId = Guid.NewGuid(),
            MethodId = 0
        };
    }

    [Fact]
    public async Task AuthorizationDisabled_AllowsEverything()
    {
        // Arrange
        var filter = CreateFilter(new RpcSecurityOptions { EnableAuthorization = false });
        var context = CreateContext(typeof(ITestSecureGrain), nameof(ITestSecureGrain.SecureMethod));

        // Act
        var result = await filter.AuthorizeAsync(context);

        // Assert
        Assert.True(result.IsAuthorized);
        Assert.Equal("Authorization disabled", result.DecidingRule);
    }

    [Fact]
    public async Task AllowAnonymous_BypassesAuthorization()
    {
        // Arrange
        var filter = CreateFilter();
        var context = CreateContext(typeof(ITestSecureGrain), nameof(ITestSecureGrain.PublicMethod));

        // Act
        var result = await filter.AuthorizeAsync(context);

        // Assert
        Assert.True(result.IsAuthorized);
        Assert.Equal("[AllowAnonymous]", result.DecidingRule);
    }

    [Fact]
    public async Task Authorize_DeniesAnonymousUser()
    {
        // Arrange
        var filter = CreateFilter();
        var context = CreateContext(typeof(ITestSecureGrain), nameof(ITestSecureGrain.SecureMethod));

        // Act
        var result = await filter.AuthorizeAsync(context);

        // Assert
        Assert.False(result.IsAuthorized);
        Assert.Contains("Authentication required", result.FailureReason);
    }

    [Fact]
    public async Task Authorize_AllowsAuthenticatedUser()
    {
        // Arrange
        var filter = CreateFilter();
        var user = new RpcUserIdentity
        {
            UserId = "player-123",
            UserName = "TestPlayer",
            Role = UserRole.User
        };
        var context = CreateContext(typeof(ITestSecureGrain), nameof(ITestSecureGrain.SecureMethod), user);

        // Act
        var result = await filter.AuthorizeAsync(context);

        // Assert
        Assert.True(result.IsAuthorized);
    }

    [Fact]
    public async Task ServerOnly_DeniesClientUser()
    {
        // Arrange
        var filter = CreateFilter();
        var user = new RpcUserIdentity
        {
            UserId = "player-123",
            UserName = "TestPlayer",
            Role = UserRole.User
        };
        var context = CreateContext(typeof(ITestSecureGrain), nameof(ITestSecureGrain.ServerOnlyMethod), user);

        // Act
        var result = await filter.AuthorizeAsync(context);

        // Assert
        Assert.False(result.IsAuthorized);
        Assert.Contains("server components", result.FailureReason);
    }

    [Fact]
    public async Task ServerOnly_AllowsServerRole()
    {
        // Arrange
        var filter = CreateFilter();
        var user = new RpcUserIdentity
        {
            UserId = "action-server-1",
            UserName = "ActionServer",
            Role = UserRole.Server
        };
        var context = CreateContext(typeof(ITestSecureGrain), nameof(ITestSecureGrain.ServerOnlyMethod), user);

        // Act
        var result = await filter.AuthorizeAsync(context);

        // Assert
        Assert.True(result.IsAuthorized);
    }

    [Fact]
    public async Task RequireRole_DeniesInsufficientRole()
    {
        // Arrange
        var filter = CreateFilter();
        var user = new RpcUserIdentity
        {
            UserId = "player-123",
            UserName = "TestPlayer",
            Role = UserRole.Guest
        };
        var context = CreateContext(typeof(ITestSecureGrain), nameof(ITestSecureGrain.RequireUserMethod), user);

        // Act
        var result = await filter.AuthorizeAsync(context);

        // Assert
        Assert.False(result.IsAuthorized);
        Assert.Contains("Role", result.FailureReason);
    }

    [Fact]
    public async Task RequireRole_AllowsSufficientRole()
    {
        // Arrange
        var filter = CreateFilter();
        var user = new RpcUserIdentity
        {
            UserId = "player-123",
            UserName = "TestPlayer",
            Role = UserRole.User
        };
        var context = CreateContext(typeof(ITestSecureGrain), nameof(ITestSecureGrain.RequireUserMethod), user);

        // Act
        var result = await filter.AuthorizeAsync(context);

        // Assert
        Assert.True(result.IsAuthorized);
    }

    [Fact]
    public async Task DefaultPolicy_RequireAuthentication_DeniesAnonymous()
    {
        // Arrange
        var filter = CreateFilter(new RpcSecurityOptions
        {
            DefaultPolicy = DefaultAuthorizationPolicy.RequireAuthentication
        });
        var context = CreateContext(typeof(ITestNoAttributeGrain), nameof(ITestNoAttributeGrain.SomeMethod));

        // Act
        var result = await filter.AuthorizeAsync(context);

        // Assert
        Assert.False(result.IsAuthorized);
    }

    [Fact]
    public async Task DefaultPolicy_AllowAnonymous_AllowsAnonymous()
    {
        // Arrange
        var filter = CreateFilter(new RpcSecurityOptions
        {
            DefaultPolicy = DefaultAuthorizationPolicy.AllowAnonymous
        });
        var context = CreateContext(typeof(ITestNoAttributeGrain), nameof(ITestNoAttributeGrain.SomeMethod));

        // Act
        var result = await filter.AuthorizeAsync(context);

        // Assert
        Assert.True(result.IsAuthorized);
    }

    [Fact]
    public async Task ClientAccessible_DeniesClientWhenStrictModeEnabled()
    {
        // Arrange
        var filter = CreateFilter(new RpcSecurityOptions
        {
            EnforceClientAccessibleAttribute = true
        });
        var user = new RpcUserIdentity
        {
            UserId = "player-123",
            UserName = "TestPlayer",
            Role = UserRole.User
        };
        var context = CreateContext(typeof(ITestNotClientAccessibleGrain), nameof(ITestNotClientAccessibleGrain.SomeMethod), user);

        // Act
        var result = await filter.AuthorizeAsync(context);

        // Assert
        Assert.False(result.IsAuthorized);
        Assert.Contains("not accessible to clients", result.FailureReason);
    }

    [Fact]
    public async Task ClientAccessible_AllowsServerRegardless()
    {
        // Arrange
        var filter = CreateFilter(new RpcSecurityOptions
        {
            EnforceClientAccessibleAttribute = true
        });
        var user = new RpcUserIdentity
        {
            UserId = "action-server-1",
            UserName = "ActionServer",
            Role = UserRole.Server
        };
        var context = CreateContext(typeof(ITestNotClientAccessibleGrain), nameof(ITestNotClientAccessibleGrain.SomeMethod), user);

        // Act
        var result = await filter.AuthorizeAsync(context);

        // Assert
        Assert.True(result.IsAuthorized);
    }

    #region Test Interfaces

    [Authorize]
    private interface ITestSecureGrain
    {
        [AllowAnonymous]
        Task PublicMethod();

        Task SecureMethod();

        [ServerOnly]
        Task ServerOnlyMethod();

        [RequireRole(UserRole.User)]
        Task RequireUserMethod();

        [RequireRole(UserRole.Admin)]
        Task RequireAdminMethod();
    }

    private interface ITestNoAttributeGrain
    {
        Task SomeMethod();
    }

    [Authorize]
    private interface ITestNotClientAccessibleGrain
    {
        Task SomeMethod();
    }

    [ClientAccessible]
    [Authorize]
    private interface ITestClientAccessibleGrain
    {
        Task SomeMethod();
    }

    #endregion
}
