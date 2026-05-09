using Alfred.Gateway.Endpoints;
using Scalar.AspNetCore;

namespace Alfred.Gateway.Extensions;

/// <summary>
/// Extension methods for configuring the gateway documentation portal and per-service Scalar pages.
/// </summary>
public static class ScalarExtensions
{
    /// <summary>
    /// Registers documentation services used by the gateway docs portal.
    /// </summary>
    public static IServiceCollection AddAlfredScalar(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();

        return services;
    }

    /// <summary>
    /// Uses a gateway docs landing page plus dedicated Scalar pages for each downstream service.
    /// </summary>
    public static WebApplication UseAlfredScalar(this WebApplication app)
    {
        var dynamicProxies = app.Services.GetRequiredService<IReadOnlyList<DynamicProxyDefinition>>();

        app.MapDocumentationPortal();

        foreach (var service in DocumentationPortalEndpoints.BuildServiceDocs(dynamicProxies))
        {
            app.MapScalarApiReference(service.DocsPath, c =>
            {
                c.Title = $"{service.DisplayName} API";
                c.Theme = ScalarTheme.Purple;
                c.DefaultHttpClient =
                    new KeyValuePair<ScalarTarget, ScalarClient>(ScalarTarget.CSharp, ScalarClient.HttpClient);
                c.OpenApiRoutePattern = service.OpenApiRoutePattern;
                c.PersistentAuthentication = true;
            });
        }

        return app;
    }
}
