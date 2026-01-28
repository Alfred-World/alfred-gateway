namespace Alfred.Gateway.Attributes;

/// <summary>
/// Attribute to specify required permission for an endpoint.
/// Used by DynamicAuthorizationMiddleware to check permissions from Redis cache.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public class RequirePermissionAttribute : Attribute
{
    /// <summary>
    /// The permission code required to access this endpoint.
    /// Example: "finance:write", "users:read", "admin:*"
    /// </summary>
    public string Permission { get; }

    public RequirePermissionAttribute(string permission)
    {
        Permission = permission ?? throw new ArgumentNullException(nameof(permission));
    }
}