namespace Alfred.Gateway.Configuration;

/// <summary>
/// Centralized configuration for gateway settings loaded from environment variables.
/// Service routing is env-driven via DYNAMIC_PROXY__* entries.
/// </summary>
public class GatewayConfiguration
{
    // Application Settings
    public string AppHostname { get; }
    public int AppPort { get; }
    public string Environment { get; }
    public bool IsDevelopment => Environment.Equals("Development", StringComparison.OrdinalIgnoreCase);

    // Authentication Settings
    // AUTH_AUTHORITY: base URL used for JWKS discovery (/.well-known/jwks.json).
    // Falls back to IDENTITY_SERVICE_URL for backward compatibility.
    public string AuthAuthority { get; }
    public string AuthValidIssuer { get; }
    public bool AuthRequireHttpsMetadata { get; }

    // CORS Settings
    public string[] CorsAllowedOrigins { get; }

    // Rate Limiting Settings
    public bool RateLimitEnabled { get; }
    public int RateLimitWindowMinutes { get; }
    public int RateLimitPermitLimit { get; }
    public int RateLimitQueueLimit { get; }

    // Health Check Settings
    public int HealthCheckIntervalSeconds { get; }
    public int HealthCheckTimeoutSeconds { get; }

    public GatewayConfiguration()
    {
        Environment = GetOptional("ASPNETCORE_ENVIRONMENT") ?? "Development";
        AppHostname = GetOptional("APP_HOSTNAME") ?? "localhost";
        AppPort = GetInt("APP_PORT", 8000);
        ValidatePort(AppPort);

        AuthAuthority = GetOptional("AUTH_AUTHORITY")
                        ?? GetOptional("IDENTITY_SERVICE_URL")
                        ?? "http://localhost:8100";
        AuthValidIssuer = GetRequired("AUTH_VALID_ISSUER");
        AuthRequireHttpsMetadata = GetBool("AUTH_REQUIRE_HTTPS_METADATA", false);

        var corsOrigins = GetOptional("CORS_ALLOWED_ORIGINS") ?? "http://localhost:7000";
        CorsAllowedOrigins = corsOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(o => o.Trim())
            .ToArray();

        RateLimitEnabled = GetBool("RATE_LIMIT_ENABLED", !IsDevelopment);
        RateLimitWindowMinutes = GetInt("RATE_LIMIT_WINDOW_MINUTES", 1);
        RateLimitPermitLimit = GetInt("RATE_LIMIT_PERMIT_LIMIT", 100);
        RateLimitQueueLimit = GetInt("RATE_LIMIT_QUEUE_LIMIT", 2);

        HealthCheckIntervalSeconds = GetInt("HEALTH_CHECK_INTERVAL_SECONDS", 30);
        HealthCheckTimeoutSeconds = GetInt("HEALTH_CHECK_TIMEOUT_SECONDS", 10);

        ValidateConfiguration();
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(AppHostname))
            throw new InvalidOperationException("APP_HOSTNAME cannot be empty");

        if (string.IsNullOrWhiteSpace(AuthAuthority))
            throw new InvalidOperationException("AUTH_AUTHORITY cannot be empty");

        if (CorsAllowedOrigins.Length == 0)
            throw new InvalidOperationException("CORS_ALLOWED_ORIGINS must contain at least one origin");
    }

    private static void ValidatePort(int port)
    {
        if (port <= 0 || port > 65535)
            throw new InvalidOperationException($"Port must be between 1 and 65535. Got: {port}");
    }

    private static string GetRequired(string key)
    {
        var value = System.Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"Required environment variable '{key}' is not set. Please check your .env file.");

        return value;
    }

    private static string? GetOptional(string key)
    {
        return System.Environment.GetEnvironmentVariable(key);
    }

    private static int GetInt(string key, int defaultValue)
    {
        var value = GetOptional(key);
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;

        if (!int.TryParse(value, out var result))
            throw new InvalidOperationException(
                $"Environment variable '{key}' must be a valid integer. Got: '{value}'");

        return result;
    }

    private static bool GetBool(string key, bool defaultValue)
    {
        var value = GetOptional(key);
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;

        if (!bool.TryParse(value, out var result))
            throw new InvalidOperationException(
                $"Environment variable '{key}' must be a valid boolean. Got: '{value}'");

        return result;
    }
}
