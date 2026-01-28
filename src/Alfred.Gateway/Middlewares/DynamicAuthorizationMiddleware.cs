using System.Security.Claims;
using System.Text.Json;
using Alfred.Gateway.Attributes;
using StackExchange.Redis;

namespace Alfred.Gateway.Middlewares;

/// <summary>
/// Dynamic Authorization Middleware - checks permissions from Redis cache.
/// Flow:
/// 1. Parse JWT -> get Role(s)
/// 2. Query Redis for permissions:ROLE_NAME
/// 3. Check if user has required permission
/// 4. Return 403 Forbidden if missing
/// </summary>
public class DynamicAuthorizationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<DynamicAuthorizationMiddleware> _logger;
    private readonly string _cacheKeyPrefix = "permissions:";

    public DynamicAuthorizationMiddleware(
        RequestDelegate next,
        ILogger<DynamicAuthorizationMiddleware> logger,
        IConnectionMultiplexer? redis = null)
    {
        _next = next;
        _logger = logger;
        _redis = redis;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip if user is not authenticated
        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            await _next(context);
            return;
        }

        // Get roles from JWT claims
        var roles = context.User.FindAll(ClaimTypes.Role)
            .Select(c => c.Value.ToUpperInvariant())
            .ToList();

        // If no roles found, try alternative claim type
        if (roles.Count == 0)
            roles = context.User.FindAll("role")
                .Select(c => c.Value.ToUpperInvariant())
                .ToList();

        // Check for Owner role - Owner has ALL permissions (bypass check)
        if (roles.Contains("OWNER"))
        {
            await _next(context);
            return;
        }

        // Get all permissions for user's roles from Redis
        var userPermissions = new HashSet<string>();

        if (_redis != null)
        {
            var db = _redis.GetDatabase();

            foreach (var role in roles)
                try
                {
                    var cacheKey = $"{_cacheKeyPrefix}{role}";
                    var json = await db.StringGetAsync(cacheKey);

                    if (json.HasValue)
                    {
                        var permissions = JsonSerializer.Deserialize<List<string>>(json.ToString());
                        if (permissions != null)
                            foreach (var perm in permissions)
                                userPermissions.Add(perm);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get permissions for role {Role} from Redis", role);
                }
        }

        // Check if user has wildcard permission ("*" = all access)
        if (userPermissions.Contains("*"))
        {
            await _next(context);
            return;
        }

        // Get required permission from endpoint metadata (if any)
        var endpoint = context.GetEndpoint();
        var requiredPermissions = endpoint?.Metadata
            .GetOrderedMetadata<RequirePermissionAttribute>()
            .Select(a => a.Permission.ToLowerInvariant())
            .ToList() ?? new List<string>();

        // If no specific permission required, allow access
        if (requiredPermissions.Count == 0)
        {
            await _next(context);
            return;
        }

        // Check if user has at least one of the required permissions
        var hasPermission = requiredPermissions.Any(required =>
            userPermissions.Contains(required) ||
            userPermissions.Any(up => MatchesWildcard(up, required)));

        if (!hasPermission)
        {
            _logger.LogWarning(
                "Access denied for user with roles [{Roles}]. Required: [{Required}], Has: [{Has}]",
                string.Join(", ", roles),
                string.Join(", ", requiredPermissions),
                string.Join(", ", userPermissions));

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";

            var response = JsonSerializer.Serialize(new
            {
                success = false,
                errors = new[]
                {
                    new
                    {
                        message = "You don't have permission to access this resource.",
                        code = "FORBIDDEN",
                        requiredPermissions = requiredPermissions
                    }
                }
            });

            await context.Response.WriteAsync(response);
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// Check if user permission matches required permission with wildcard support.
    /// Example: user has "finance:*" and required is "finance:read" -> match
    /// </summary>
    private static bool MatchesWildcard(string userPermission, string requiredPermission)
    {
        if (userPermission == "*") return true;

        if (userPermission.EndsWith(":*"))
        {
            var prefix = userPermission[..^2]; // Remove ":*"
            return requiredPermission.StartsWith(prefix + ":");
        }

        return false;
    }
}

/// <summary>
/// Extension methods for registering DynamicAuthorizationMiddleware
/// </summary>
public static class DynamicAuthorizationMiddlewareExtensions
{
    public static IApplicationBuilder UseDynamicAuthorization(this IApplicationBuilder app)
    {
        return app.UseMiddleware<DynamicAuthorizationMiddleware>();
    }
}