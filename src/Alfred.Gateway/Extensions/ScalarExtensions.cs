using Alfred.Gateway.Middlewares;
using Microsoft.OpenApi.Models;
using Scalar.AspNetCore;

namespace Alfred.Gateway.Extensions;

/// <summary>
/// Extension methods for configuring Scalar API documentation with API aggregation
/// </summary>
public static class ScalarExtensions
{
    /// <summary>
    /// Adds OpenAPI spec generation with aggregated API documentation from backend services
    /// </summary>
    public static IServiceCollection AddAlfredScalar(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddHttpClient("OpenApiAggregator")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            });

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
                Description =
                    "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token",
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
    /// Uses Scalar API reference with aggregated backend services
    /// </summary>
    public static WebApplication UseAlfredScalar(this WebApplication app)
    {
        // Serve gateway's own OpenAPI spec (used internally by aggregator)
        app.UseSwagger();

        // Custom endpoint that merges gateway + backend service specs
        app.MapAggregatedOpenApi();

        // Single Scalar page showing all APIs
        app.MapScalarApiReference("/docs", c =>
        {
            c.Title = "Alfred API Gateway";
            c.Theme = ScalarTheme.Purple;
            c.DefaultHttpClient = new(ScalarTarget.CSharp, ScalarClient.HttpClient);
            c.OpenApiRoutePattern = "/api-docs/{documentName}.json";
            c.PersistentAuthentication = true;
        });

        return app;
    }
}
