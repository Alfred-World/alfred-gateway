using Microsoft.OpenApi.Models;

namespace Alfred.Gateway.Extensions;

/// <summary>
/// Extension methods for configuring Swagger with API aggregation
/// </summary>
public static class SwaggerExtensions
{
    /// <summary>
    /// Adds Swagger with aggregated API documentation from backend services
    /// </summary>
    public static IServiceCollection AddAlfredSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Alfred API Gateway",
                Version = "v1",
                Description = "Centralized API Gateway for A.L.F.R.E.D system - aggregates all microservices APIs",
                Contact = new OpenApiContact
                {
                    Name = "Alfred Development Team",
                    Email = "dev@alfred.com"
                }
            });

            // Add JWT Bearer authentication
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    new List<string>()
                }
            });
        });

        return services;
    }

    /// <summary>
    /// Uses Swagger UI with aggregated backend services
    /// </summary>
    public static IApplicationBuilder UseAlfredSwagger(this IApplicationBuilder app, IConfiguration configuration)
    {
        app.UseSwagger();
        
        app.UseSwaggerUI(options =>
        {
            // Add cache-busting query parameter
            var cacheBuster = $"?v={DateTime.UtcNow.Ticks}";
            
            // Gateway's own swagger
            options.SwaggerEndpoint($"/swagger/v1/swagger.json{cacheBuster}", "Gateway API v1");
            
            // Aggregated backend services swagger - use proxy routes through gateway
            // These will be proxied through YARP to avoid CORS issues
            options.SwaggerEndpoint($"/api/identity/swagger/v1/swagger.json{cacheBuster}", "Identity Service API v1");
            options.SwaggerEndpoint($"/api/core/swagger/v1/swagger.json{cacheBuster}", "Core Service API v1");
            
            options.RoutePrefix = "swagger";
            options.DocumentTitle = "Alfred API Gateway - API Documentation";
            options.DisplayRequestDuration();
            options.EnableDeepLinking();
            options.EnableFilter();
            options.ShowExtensions();
        });

        return app;
    }
}
