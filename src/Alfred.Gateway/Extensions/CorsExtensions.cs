using Alfred.Gateway.Configuration;

namespace Alfred.Gateway.Extensions;

/// <summary>
/// Extension methods for configuring CORS
/// </summary>
public static class CorsExtensions
{
    private const string DefaultPolicyName = "AlfredCorsPolicy";

    /// <summary>
    /// Adds CORS configuration for Alfred Gateway
    /// </summary>
    public static IServiceCollection AddAlfredCors(this IServiceCollection services, GatewayConfiguration config)
    {
        services.AddCors(options =>
        {
            options.AddPolicy(DefaultPolicyName, builder =>
            {
                builder
                    .WithOrigins(config.CorsAllowedOrigins)
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials() // Cho phép gửi Cookie/Token
                    .SetIsOriginAllowedToAllowWildcardSubdomains(); // Cho phép subdomain
            });

            // Policy cho Development - Cho phép tất cả
            options.AddPolicy("AllowAll", builder =>
            {
                builder
                    .AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader();
            });
        });

        return services;
    }

    /// <summary>
    /// Uses CORS middleware with the default policy
    /// </summary>
    public static IApplicationBuilder UseAlfredCors(this IApplicationBuilder app)
    {
        app.UseCors(DefaultPolicyName);
        return app;
    }
}
