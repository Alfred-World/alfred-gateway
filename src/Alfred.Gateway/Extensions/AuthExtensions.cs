using System.Text.Json;
using Alfred.Gateway.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Alfred.Gateway.Extensions;

/// <summary>
/// Extension methods for configuring Authentication & Authorization
/// </summary>
public static class AuthExtensions
{
    /// <summary>
    /// Adds JWT Bearer authentication and authorization policies
    /// </summary>
    public static IServiceCollection AddAlfredAuth(this IServiceCollection services, GatewayConfiguration config)
    {
        // Add Authentication with JWT Bearer
        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                // Identity Server configuration from env
                options.Authority = config.AuthAuthority;
                options.RequireHttpsMetadata = config.AuthRequireHttpsMetadata;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = config.AuthValidIssuer,
                    ValidateAudience = false, // Có thể bật lên nếu cần validate audience
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromMinutes(5) // Cho phép sai lệch thời gian 5 phút
                };

                // Event handlers for debugging and custom logic
                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        if (context.Exception.GetType() == typeof(SecurityTokenExpiredException))
                            context.Response.Headers.Add("Token-Expired", "true");
                        return Task.CompletedTask;
                    },
                    OnChallenge = context =>
                    {
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
}