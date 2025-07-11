# Granville RPC Security Implementation Guide

## Overview
This guide provides step-by-step instructions for implementing security features in Granville RPC applications. It covers practical examples and best practices for developers.

## Quick Start

### 1. Basic Authentication Setup

```csharp
// Program.cs - Server setup
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGranvilleRpc()
    .AddAuthentication(options =>
    {
        options.DefaultScheme = "JWT";
        options.TokenExpiration = TimeSpan.FromMinutes(30);
    })
    .AddJwtAuthentication(jwt =>
    {
        jwt.SigningKey = Configuration["JWT:SigningKey"];
        jwt.Issuer = "your-app";
        jwt.Audience = "your-app-users";
    })
    .AddAuthorization(options =>
    {
        options.DefaultPolicy = "RequireAuthentication";
    });

var app = builder.Build();
app.UseGranvilleRpc();
app.Run();
```

### 2. Client Authentication

```csharp
// Client connection with authentication
var client = new RpcClient(new RpcClientOptions
{
    ServerAddress = "server.example.com:5000",
    Authentication = new AuthenticationOptions
    {
        Credentials = new UsernamePasswordCredentials("user", "password")
    }
});

await client.ConnectAsync();

// The client will automatically handle token refresh
```

## Implementing Secure RpcGrains

### Basic Secured Grain

```csharp
[RpcGrain]
[RequireAuthentication] // Requires any authenticated user
public class UserProfileGrain : RpcGrain
{
    private UserProfile _profile;
    
    public override Task OnActivateAsync()
    {
        // Load user profile based on authenticated user
        var userId = this.GetUserId();
        _profile = await LoadUserProfile(userId);
        return base.OnActivateAsync();
    }
    
    [AllowAnonymous]
    public Task<string> GetPublicDisplayName()
    {
        return Task.FromResult(_profile.DisplayName);
    }
    
    [RequireOwner] // Only the profile owner can update
    public async Task UpdateProfile(UserProfileUpdate update)
    {
        _profile.Bio = update.Bio;
        _profile.Avatar = update.Avatar;
        await SaveUserProfile(_profile);
    }
    
    [RequireRole("admin")]
    public async Task BanUser(string reason)
    {
        _profile.IsBanned = true;
        _profile.BanReason = reason;
        await SaveUserProfile(_profile);
    }
}
```

### Game-Specific Security

```csharp
[RpcGrain]
public class GameSessionGrain : RpcGrain
{
    private GameSession _session;
    
    [RequireRole("server", "game-host")]
    public async Task<string> CreateSession(GameConfig config)
    {
        _session = new GameSession
        {
            Id = Guid.NewGuid().ToString(),
            Config = config,
            CreatedBy = this.GetUserId(),
            CreatedAt = DateTime.UtcNow
        };
        
        await SaveSession(_session);
        return _session.Id;
    }
    
    [RequireCustomAuthorization(typeof(GameJoinAuthorizer))]
    public async Task<JoinResult> JoinGame(string playerId)
    {
        // Custom authorizer checks:
        // - Is game full?
        // - Is player banned?
        // - Does player meet requirements?
        // - Is player already in another game?
        
        if (_session.Players.Count >= _session.Config.MaxPlayers)
        {
            return JoinResult.Failed("Game is full");
        }
        
        _session.Players.Add(new Player { Id = playerId });
        await SaveSession(_session);
        
        return JoinResult.Success();
    }
    
    [RequireParticipant] // Custom attribute checking if user is in the game
    public async Task<GameState> GetGameState()
    {
        return _session.CurrentState;
    }
}

// Custom authorization attribute
public class RequireParticipantAttribute : AuthorizeAttribute
{
    protected override Task<bool> IsAuthorizedAsync(
        AuthorizationContext context)
    {
        var grain = context.Grain as GameSessionGrain;
        var userId = context.User.GetUserId();
        
        return Task.FromResult(
            grain._session.Players.Any(p => p.Id == userId));
    }
}

// Custom authorizer for complex logic
public class GameJoinAuthorizer : IAuthorizationHandler
{
    private readonly IPlayerService _playerService;
    private readonly IGameService _gameService;
    
    public async Task<AuthorizationResult> AuthorizeAsync(
        AuthorizationContext context)
    {
        var playerId = context.User.GetUserId();
        
        // Check if player is banned
        if (await _playerService.IsPlayerBanned(playerId))
        {
            return AuthorizationResult.Fail("Player is banned");
        }
        
        // Check if player is already in a game
        if (await _gameService.IsPlayerInGame(playerId))
        {
            return AuthorizationResult.Fail("Already in another game");
        }
        
        // Check level requirements
        var grain = context.Grain as GameSessionGrain;
        var playerLevel = await _playerService.GetPlayerLevel(playerId);
        
        if (playerLevel < grain._session.Config.MinLevel)
        {
            return AuthorizationResult.Fail($"Requires level {grain._session.Config.MinLevel}");
        }
        
        return AuthorizationResult.Success();
    }
}
```

## Implementing Transport Security

### DTLS Configuration

```csharp
// Server DTLS setup
builder.Services.AddGranvilleRpc()
    .AddDtlsTransport(options =>
    {
        options.Certificate = LoadServerCertificate();
        options.RequireClientCertificate = false; // For server-to-server, set to true
        options.EnableSessionResumption = true;
        options.HandshakeTimeout = TimeSpan.FromSeconds(5);
    });

// Client DTLS setup
var client = new RpcClient(new RpcClientOptions
{
    Transport = new DtlsTransportOptions
    {
        ServerCertificateValidation = ValidateServerCertificate,
        ClientCertificate = null // or provide for mutual TLS
    }
});

private bool ValidateServerCertificate(X509Certificate2 cert, X509Chain chain)
{
    // Implement certificate validation logic
    // For production, properly validate the certificate
    return true;
}
```

### Message-Level Security

```csharp
// Enable message signing and encryption
builder.Services.AddGranvilleRpc()
    .AddMessageSecurity(options =>
    {
        options.EnableSigning = true;
        options.EnableEncryption = true;
        options.Algorithm = MessageSecurityAlgorithm.AES256_HMACSHA256;
        options.KeyRotationInterval = TimeSpan.FromDays(1);
    });
```

## Rate Limiting Implementation

### Basic Rate Limiting

```csharp
// Configure rate limiting
builder.Services.AddGranvilleRpc()
    .AddRateLimiting(options =>
    {
        // Global limits
        options.GlobalRequestsPerSecond = 1000;
        options.PerIpRequestsPerSecond = 100;
        
        // Per-user limits
        options.PerUserRequestsPerSecond = 50;
        options.BurstSize = 10;
        
        // Method-specific limits
        options.MethodLimits["ExpensiveOperation"] = new RateLimit
        {
            RequestsPerMinute = 5,
            BurstSize = 1
        };
    });

// Custom rate limit for specific grain
[RpcGrain]
[RateLimit(RequestsPerMinute = 100, BurstSize = 20)]
public class ChatGrain : RpcGrain
{
    [RateLimit(RequestsPerMinute = 30)] // More restrictive for sending
    public async Task SendMessage(string message)
    {
        // Process message
    }
    
    // Uses grain-level rate limit
    public async Task<List<Message>> GetMessages()
    {
        // Return messages
    }
}
```

### Advanced Rate Limiting with Redis

```csharp
// Use Redis for distributed rate limiting
builder.Services.AddStackExchangeRedis("localhost:6379");
builder.Services.AddGranvilleRpc()
    .AddDistributedRateLimiting(options =>
    {
        options.UseRedis();
        options.KeyPrefix = "ratelimit:";
        options.SlidingWindow = true;
    });
```

## Input Validation

### Declarative Validation

```csharp
[RpcGrain]
public class PlayerActionGrain : RpcGrain
{
    public async Task MovePlayer(
        [Range(-100, 100)] float x,
        [Range(-100, 100)] float y,
        [Range(0, 10)] float speed)
    {
        // Validated automatically before method execution
    }
    
    public async Task SendChat(
        [Required]
        [StringLength(200, MinimumLength = 1)]
        [NotContains(new[] { "<script", "javascript:" })]
        string message)
    {
        // Message is validated for length and XSS attempts
    }
    
    public async Task CreateItem(
        [Valid] ItemCreationRequest request)
    {
        // Complex object validation
    }
}

public class ItemCreationRequest : IValidatable
{
    [Required]
    [StringLength(50)]
    public string Name { get; set; }
    
    [Range(1, 1000000)]
    public int Value { get; set; }
    
    [EnumDataType(typeof(ItemRarity))]
    public ItemRarity Rarity { get; set; }
    
    public ValidationResult Validate()
    {
        var errors = new List<string>();
        
        if (Rarity == ItemRarity.Legendary && Value < 10000)
        {
            errors.Add("Legendary items must have value >= 10000");
        }
        
        if (Name.Contains("admin", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Item name cannot contain 'admin'");
        }
        
        return errors.Any() 
            ? ValidationResult.Failed(errors) 
            : ValidationResult.Success();
    }
}
```

### Custom Validators

```csharp
// Custom validation attribute
public class SafeFileNameAttribute : ValidationAttribute
{
    protected override ValidationResult IsValid(
        object value, 
        ValidationContext context)
    {
        if (value is string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            if (fileName.Any(c => invalidChars.Contains(c)))
            {
                return new ValidationResult("Invalid characters in filename");
            }
            
            if (fileName.Contains(".."))
            {
                return new ValidationResult("Path traversal detected");
            }
        }
        
        return ValidationResult.Success;
    }
}

// Usage
public async Task SaveFile(
    [SafeFileName] string filename,
    byte[] content)
{
    // Filename is validated for security issues
}
```

## Secure Session Management

### Session Implementation

```csharp
public class SecureSessionManager : ISessionManager
{
    private readonly IDistributedCache _cache;
    private readonly IDataProtector _protector;
    
    public async Task<Session> CreateSessionAsync(
        string userId, 
        Dictionary<string, object> claims)
    {
        var session = new Session
        {
            Id = GenerateSecureSessionId(),
            UserId = userId,
            Claims = claims,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
            IpAddress = GetClientIpAddress(),
            UserAgent = GetUserAgent()
        };
        
        // Encrypt session data
        var encrypted = _protector.Protect(JsonSerializer.Serialize(session));
        
        // Store in distributed cache
        await _cache.SetStringAsync(
            $"session:{session.Id}", 
            encrypted,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = session.ExpiresAt
            });
            
        return session;
    }
    
    public async Task<Session> ValidateSessionAsync(string sessionId)
    {
        var encrypted = await _cache.GetStringAsync($"session:{sessionId}");
        if (encrypted == null)
        {
            throw new InvalidSessionException("Session not found");
        }
        
        var json = _protector.Unprotect(encrypted);
        var session = JsonSerializer.Deserialize<Session>(json);
        
        // Validate session properties
        if (session.ExpiresAt < DateTime.UtcNow)
        {
            await InvalidateSessionAsync(sessionId);
            throw new InvalidSessionException("Session expired");
        }
        
        // Optional: Check IP address hasn't changed
        if (session.IpAddress != GetClientIpAddress())
        {
            // Log potential session hijack
            await LogSecurityEvent("Session IP mismatch", session);
        }
        
        // Sliding expiration
        session.ExpiresAt = DateTime.UtcNow.AddMinutes(30);
        await UpdateSessionAsync(session);
        
        return session;
    }
    
    private string GenerateSecureSessionId()
    {
        var bytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }
}
```

## Monitoring and Auditing

### Security Event Logging

```csharp
public class SecurityEventLogger : ISecurityEventLogger
{
    private readonly ILogger<SecurityEventLogger> _logger;
    private readonly ISecurityEventStore _eventStore;
    
    public async Task LogAuthenticationAsync(
        string userId, 
        bool success, 
        string reason = null)
    {
        var evt = new SecurityEvent
        {
            Type = SecurityEventType.Authentication,
            UserId = userId,
            Success = success,
            Reason = reason,
            IpAddress = GetClientIpAddress(),
            UserAgent = GetUserAgent(),
            Timestamp = DateTime.UtcNow
        };
        
        // Structured logging for analysis
        _logger.LogInformation(
            "Authentication {Result} for user {UserId} from {IpAddress}",
            success ? "succeeded" : "failed",
            userId,
            evt.IpAddress);
            
        // Store for audit trail
        await _eventStore.StoreAsync(evt);
        
        // Alert on suspicious activity
        if (!success)
        {
            await CheckForBruteForceAsync(userId, evt.IpAddress);
        }
    }
    
    public async Task LogAuthorizationAsync(
        string userId,
        string resource,
        string action,
        bool allowed)
    {
        var evt = new SecurityEvent
        {
            Type = SecurityEventType.Authorization,
            UserId = userId,
            Resource = resource,
            Action = action,
            Success = allowed,
            Timestamp = DateTime.UtcNow
        };
        
        await _eventStore.StoreAsync(evt);
        
        // Alert on privilege escalation attempts
        if (!allowed && IsPrivilegedResource(resource))
        {
            await AlertSecurityTeam(
                $"Unauthorized access attempt to {resource} by {userId}");
        }
    }
    
    private async Task CheckForBruteForceAsync(
        string userId, 
        string ipAddress)
    {
        var recentFailures = await _eventStore.GetFailedLoginsAsync(
            userId, 
            ipAddress, 
            TimeSpan.FromMinutes(15));
            
        if (recentFailures.Count > 5)
        {
            await BlockIpAddressAsync(ipAddress, TimeSpan.FromHours(1));
            await AlertSecurityTeam(
                $"Possible brute force attack from {ipAddress}");
        }
    }
}
```

### Real-time Security Monitoring

```csharp
// Security metrics collection
builder.Services.AddGranvilleRpc()
    .AddSecurityMetrics(options =>
    {
        options.EnableRealTimeAlerts = true;
        options.AlertThresholds = new AlertThresholds
        {
            FailedAuthsPerMinute = 10,
            UnauthorizedAccessPerMinute = 20,
            AnomalousRequestsPerMinute = 50
        };
    })
    .AddOpenTelemetry(otel =>
    {
        otel.AddSecurityInstrumentation();
        otel.AddOtlpExporter(options =>
        {
            options.Endpoint = "http://localhost:4317";
        });
    });
```

## Production Deployment Checklist

### Pre-deployment Security Review

```csharp
// Security configuration validator
public class SecurityConfigurationValidator
{
    public ValidationResult ValidateConfiguration(IConfiguration config)
    {
        var errors = new List<string>();
        
        // Check authentication is enabled
        if (!config.GetValue<bool>("Authentication:Enabled"))
        {
            errors.Add("Authentication must be enabled in production");
        }
        
        // Check for secure keys
        var signingKey = config["JWT:SigningKey"];
        if (string.IsNullOrEmpty(signingKey) || signingKey.Length < 32)
        {
            errors.Add("JWT signing key must be at least 32 characters");
        }
        
        // Check HTTPS/DTLS is required
        if (!config.GetValue<bool>("Security:RequireEncryption"))
        {
            errors.Add("Encryption must be required in production");
        }
        
        // Check rate limiting is configured
        if (!config.GetValue<bool>("RateLimiting:Enabled"))
        {
            errors.Add("Rate limiting must be enabled in production");
        }
        
        return errors.Any() 
            ? ValidationResult.Failed(errors)
            : ValidationResult.Success();
    }
}
```

### Security Headers and Middleware

```csharp
// Add security headers
app.Use(async (context, next) =>
{
    context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Add("X-Frame-Options", "DENY");
    context.Response.Headers.Add("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Add("Referrer-Policy", "no-referrer");
    
    await next();
});

// Add request sanitization
app.UseRequestSanitization(options =>
{
    options.MaxRequestSize = 1024 * 1024; // 1MB
    options.SanitizeHeaders = true;
    options.RemoveDangerousCharacters = true;
});
```

## Common Security Patterns

### Secure by Default Pattern

```csharp
[RpcGrain]
[RequireAuthentication] // Secure by default at grain level
public abstract class SecureGameGrain : RpcGrain
{
    // All methods require authentication by default
    
    // Explicitly mark public methods
    [AllowAnonymous]
    public virtual Task<ServerInfo> GetServerInfo()
    {
        // Public information only
    }
}

// Inherit security settings
public class PlayerGrain : SecureGameGrain
{
    // Inherits authentication requirement
    
    public async Task<PlayerStats> GetStats()
    {
        // Requires authentication
    }
}
```

### Defense in Depth Pattern

```csharp
[RpcGrain]
public class SecureTransactionGrain : RpcGrain
{
    // Layer 1: Authentication
    [RequireAuthentication]
    // Layer 2: Authorization  
    [RequireRole("user")]
    // Layer 3: Rate limiting
    [RateLimit(RequestsPerMinute = 10)]
    // Layer 4: Input validation
    public async Task<TransactionResult> ProcessTransaction(
        [Valid] TransactionRequest request)
    {
        // Layer 5: Business logic validation
        if (!await ValidateBusinessRules(request))
        {
            return TransactionResult.Failed("Invalid transaction");
        }
        
        // Layer 6: Audit logging
        await LogTransaction(request);
        
        // Process transaction
        return await ExecuteTransaction(request);
    }
}
```

## Troubleshooting Common Issues

### Authentication Failures

```csharp
// Enable detailed authentication logging
builder.Services.AddGranvilleRpc()
    .AddAuthentication(options =>
    {
        options.EnableDetailedErrors = true; // Only in development!
        options.LogLevel = LogLevel.Debug;
    });

// Custom authentication error handler
public class AuthenticationErrorHandler : IAuthenticationErrorHandler
{
    public Task HandleErrorAsync(
        AuthenticationError error,
        HttpContext context)
    {
        var response = error.Type switch
        {
            AuthenticationErrorType.InvalidCredentials => 
                "Invalid username or password",
            AuthenticationErrorType.TokenExpired => 
                "Session expired, please login again",
            AuthenticationErrorType.TokenInvalid => 
                "Invalid authentication token",
            _ => "Authentication failed"
        };
        
        // Log detailed error internally
        _logger.LogWarning(
            "Authentication failed: {ErrorType} - {Details}",
            error.Type,
            error.Details);
            
        // Return generic error to client
        return context.Response.WriteAsync(response);
    }
}
```

### Performance Issues

```csharp
// Profile security overhead
public class SecurityPerformanceProfiler
{
    public async Task ProfileSecurityOverhead()
    {
        var stopwatch = new Stopwatch();
        
        // Measure authentication overhead
        stopwatch.Start();
        await AuthenticateUser();
        stopwatch.Stop();
        Console.WriteLine($"Authentication: {stopwatch.ElapsedMilliseconds}ms");
        
        // Measure authorization overhead
        stopwatch.Restart();
        await AuthorizeRequest();
        stopwatch.Stop();
        Console.WriteLine($"Authorization: {stopwatch.ElapsedMilliseconds}ms");
        
        // Measure encryption overhead
        stopwatch.Restart();
        await EncryptMessage();
        stopwatch.Stop();
        Console.WriteLine($"Encryption: {stopwatch.ElapsedMilliseconds}ms");
    }
}
```

This implementation guide provides practical examples for implementing security features in Granville RPC applications. Remember to always follow the principle of defense in depth and regularly review and update your security measures.