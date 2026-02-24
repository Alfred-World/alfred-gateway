using System.Text.Json;
using Alfred.Gateway.Configuration;

namespace Alfred.Gateway.Middlewares;

/// <summary>
/// Provides an endpoint that aggregates OpenAPI specs from backend services 
/// into a single unified document for Scalar to consume.
/// </summary>
public static class OpenApiAggregator
{
    private static readonly (string Name, string SpecPath)[] BackendServices =
    [
        ("Identity Service", "/api/identity/swagger/v1/swagger.json"),
        ("Core Service", "/api/core/swagger/v1/swagger.json")
    ];

    public static WebApplication MapAggregatedOpenApi(this WebApplication app)
    {
        app.MapGet("/api-docs/{documentName}.json", async (
            string documentName,
            HttpContext context,
            IHttpClientFactory httpClientFactory,
            GatewayConfiguration gatewayConfig,
            ILogger<GatewayConfiguration> logger) =>
        {
            var baseUrl = $"http://localhost:{gatewayConfig.AppPort}";
            var client = httpClientFactory.CreateClient("OpenApiAggregator");
            client.BaseAddress = new Uri(baseUrl);

            // 1. Fetch gateway's own OpenAPI spec
            string gatewayJson;
            try
            {
                gatewayJson = await client.GetStringAsync($"/swagger/{documentName}/swagger.json");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch gateway OpenAPI spec");
                return Results.Problem("Failed to fetch gateway OpenAPI spec");
            }

            using var gatewaySpec = JsonDocument.Parse(gatewayJson);
            var root = gatewaySpec.RootElement;

            // 2. Build merged document
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();

                // Copy top-level properties (openapi, info, servers, security, tags)
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Name is "paths" or "components")
                        continue;
                    prop.WriteTo(writer);
                }

                // -- Merge paths --
                writer.WritePropertyName("paths");
                writer.WriteStartObject();

                if (root.TryGetProperty("paths", out var gatewayPaths))
                {
                    foreach (var path in gatewayPaths.EnumerateObject())
                        path.WriteTo(writer);
                }

                // Fetch backend specs and write their paths inline
                var backendSchemas = new List<(string Name, JsonElement Value)>();

                foreach (var (serviceName, specPath) in BackendServices)
                {
                    try
                    {
                        var json = await client.GetStringAsync(specPath);
                        using var svcSpec = JsonDocument.Parse(json);

                        if (svcSpec.RootElement.TryGetProperty("paths", out var svcPaths))
                        {
                            foreach (var path in svcPaths.EnumerateObject())
                                path.WriteTo(writer);
                        }

                        // Clone schemas so they survive JsonDocument disposal
                        if (svcSpec.RootElement.TryGetProperty("components", out var svcComp) &&
                            svcComp.TryGetProperty("schemas", out var svcSchemas))
                        {
                            foreach (var schema in svcSchemas.EnumerateObject())
                                backendSchemas.Add((schema.Name, schema.Value.Clone()));
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Skipping {Service} - not available", serviceName);
                    }
                }

                writer.WriteEndObject(); // end paths

                // -- Merge components --
                writer.WritePropertyName("components");
                writer.WriteStartObject();

                if (root.TryGetProperty("components", out var gwComp))
                {
                    foreach (var comp in gwComp.EnumerateObject())
                    {
                        if (comp.Name != "schemas")
                            comp.WriteTo(writer);
                    }
                }

                // Write all schemas (gateway + backend)
                writer.WritePropertyName("schemas");
                writer.WriteStartObject();

                if (root.TryGetProperty("components", out var gwComp2) &&
                    gwComp2.TryGetProperty("schemas", out var gwSchemas))
                {
                    foreach (var schema in gwSchemas.EnumerateObject())
                    {
                        writer.WritePropertyName(schema.Name);
                        schema.Value.WriteTo(writer);
                    }
                }

                foreach (var (name, value) in backendSchemas)
                {
                    writer.WritePropertyName(name);
                    value.WriteTo(writer);
                }

                writer.WriteEndObject(); // end schemas
                writer.WriteEndObject(); // end components
                writer.WriteEndObject(); // end root
            } // writer is flushed and disposed here

            var bytes = buffer.ToArray();
            return Results.Bytes(bytes, "application/json");
        })
        .ExcludeFromDescription();

        return app;
    }
}
