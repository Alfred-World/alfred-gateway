using System.Text.Json;
using Alfred.Gateway.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;

namespace Alfred.Gateway.Extensions;

/// <summary>
/// Extension methods for configuring Authentication & Authorization
/// </summary>
public static class AuthExtensions
{
    private static IList<JsonWebKey>? _cachedKeys;
    private static DateTime _keysLastFetched = DateTime.MinValue;
    private static readonly TimeSpan KeysCacheDuration = TimeSpan.FromHours(1);

    /// <summary>
    /// Adds JWT Bearer authentication and authorization policies
    /// </summary>
    public static IServiceCollection AddAlfredAuth(this IServiceCollection services, GatewayConfiguration config)
    {
        // Pre-fetch JWKS on startup
        var jwksUrl = $"{config.AuthAuthority}/.well-known/jwks.json";

        // Add Authentication with JWT Bearer
        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                // Disable automatic metadata fetching (we'll handle keys manually)
                options.Authority = null;
                options.RequireHttpsMetadata = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = config.AuthValidIssuer,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.Zero,
                    // Custom key resolver to fetch from internal Identity Service
                    IssuerSigningKeyResolver = (token, securityToken, kid, parameters) =>
                    {
                        return GetSigningKeys(jwksUrl);
                    }
                };

                // Event handlers for debugging and custom logic
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        // ── Session / JTI blocklist check ──────────────────────────────────────
                        // The AT carries an "authorization_id" claim set by the Identity service.
                        // When a session is revoked, Identity writes "revoked:session:{id}" to Redis
                        // with a TTL equal to the AT lifetime. We reject any token whose session
                        // has been blocklisted — providing immediate revocation without waiting for
                        // the AT to expire naturally.
                        var redis = context.HttpContext.RequestServices
                            .GetService<IConnectionMultiplexer>();

                        if (redis != null)
                        {
                            var authorizationId = context.Principal?
                                .FindFirst("authorization_id")?.Value;

                            if (!string.IsNullOrEmpty(authorizationId))
                            {
                                try
                                {
                                    var db = redis.GetDatabase();
                                    var isRevoked = await db.KeyExistsAsync($"revoked:session:{authorizationId}");

                                    if (isRevoked)
                                    {
                                        context.Fail("Session has been revoked");
                                        return;
                                    }
                                }
                                catch
                                {
                                    // Redis unavailable — fail open (don't block legitimate users)
                                }
                            }
                        }
                    },
                    OnAuthenticationFailed = context =>
                    {
                        if (context.Exception.GetType() == typeof(SecurityTokenExpiredException))
                            context.Response.Headers.Append("Token-Expired", "true");
                        return Task.CompletedTask;
                    },
                    OnChallenge = context =>
                    {
                        // Allow Swagger/Scalar/Docs to proceed (e.g. for anonymous access or token refresh)
                        // without forcing a JSON error response that breaks the UI
                        if (context.Request.Path.Value?.Contains("/swagger", StringComparison.OrdinalIgnoreCase) ==
                            true ||
                            context.Request.Path.Value?.Contains("/scalar", StringComparison.OrdinalIgnoreCase) ==
                            true ||
                            context.Request.Path.Value?.Contains("/docs", StringComparison.OrdinalIgnoreCase) == true ||
                            context.Request.Path.Value?.Contains("/api-docs", StringComparison.OrdinalIgnoreCase) ==
                            true)
                            return Task.CompletedTask;

                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = "application/json";

                        var result = JsonSerializer.Serialize(new
                        {
                            success = false,
                            errors = new[]
                            {
                                new
                                {
                                    message =
                                        "You are not authorized to access this resource. Please provide a valid token.",
                                    code = "UNAUTHORIZED"
                                }
                            }
                        });

                        return context.Response.WriteAsync(result);
                    },
                    OnForbidden = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        context.Response.ContentType = "application/json";

                        var result = JsonSerializer.Serialize(new
                        {
                            success = false,
                            errors = new[]
                            {
                                new
                                {
                                    message = "You don't have permission to access this resource.",
                                    code = "FORBIDDEN"
                                }
                            }
                        });

                        return context.Response.WriteAsync(result);
                    }
                };
            });

        // Add Authorization Policies
        services.AddAuthorization(options =>
        {
            // Policy: Authenticated - Yêu cầu user phải đăng nhập
            options.AddPolicy("Authenticated", policy =>
                policy.RequireAuthenticatedUser());

            // Policy: AllowAnonymous - Cho phép truy cập không cần token
            options.AddPolicy("AllowAnonymous", policy =>
                policy.RequireAssertion(context => true));

            // Policy: Admin - Yêu cầu role Admin
            options.AddPolicy("Admin", policy =>
                policy.RequireRole("Admin"));

            // Policy: User - Yêu cầu role User
            options.AddPolicy("User", policy =>
                policy.RequireRole("User", "Admin"));
        });

        return services;
    }

    /// <summary>
    /// Fetch JWKS from Identity Service and cache the keys
    /// </summary>
    private static IEnumerable<SecurityKey> GetSigningKeys(string jwksUrl)
    {
        // Return cached keys if still valid
        if (_cachedKeys != null && DateTime.UtcNow - _keysLastFetched < KeysCacheDuration) return _cachedKeys;

        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);

            var response = httpClient.GetStringAsync(jwksUrl).GetAwaiter().GetResult();
            var jwks = new JsonWebKeySet(response);

            _cachedKeys = jwks.Keys.ToList();
            _keysLastFetched = DateTime.UtcNow;

            return _cachedKeys;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to fetch JWKS from {jwksUrl}: {ex.Message}");
            // Return cached keys if available, even if expired
            return _cachedKeys ?? Enumerable.Empty<SecurityKey>();
        }
    }
}