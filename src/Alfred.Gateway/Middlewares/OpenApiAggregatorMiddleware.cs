using System.Text.Json;
using Alfred.Gateway.Configuration;

namespace Alfred.Gateway.Middlewares;

/// <summary>
/// Provides endpoints that serve OpenAPI specs from backend services.
/// 
/// Supported document names:
///   - "identity" → Identity Service spec only
///   - "core"     → Core Service spec only
///   - "v1"       → Aggregated spec (all services merged) for Scalar UI
/// </summary>
public static class OpenApiAggregator
{
    private static readonly Dictionary<string, (string Name, string SpecPath)> ServiceMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["identity"] = ("Identity Service", "/api/identity/swagger/v1/swagger.json"),
            ["core"] = ("Core Service", "/api/core/swagger/v1/swagger.json")
        };

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

                // --- Per-service spec: /api-docs/identity.json or /api-docs/core.json ---
                if (ServiceMap.TryGetValue(documentName, out var serviceInfo))
                    return await ServeServiceSpec(client, serviceInfo.Name, serviceInfo.SpecPath, logger);

                // --- Aggregated spec: /api-docs/v1.json ---
                return await ServeAggregatedSpec(client, documentName, logger);
            })
            .ExcludeFromDescription();

        return app;
    }

    /// <summary>
    /// Serves a single backend service's OpenAPI spec directly (pass-through).
    /// </summary>
    private static async Task<IResult> ServeServiceSpec(
        HttpClient client,
        string serviceName,
        string specPath,
        ILogger logger)
    {
        try
        {
            var json = await client.GetStringAsync(specPath);
            return Results.Text(json, "application/json");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch OpenAPI spec for {Service}", serviceName);
            return Results.Problem($"Failed to fetch OpenAPI spec for {serviceName}");
        }
    }

    /// <summary>
    /// Serves the aggregated OpenAPI spec that merges gateway + all backend services.
    /// Used by Scalar UI to show all APIs in one page.
    /// </summary>
    private static async Task<IResult> ServeAggregatedSpec(
        HttpClient client,
        string documentName,
        ILogger logger)
    {
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

        // 2. Collect all backend specs in parallel
        var backendSpecs = new List<(string Name, JsonDocument Doc)>();
        var fetchTasks = ServiceMap.Select(async kvp =>
        {
            try
            {
                var json = await client.GetStringAsync(kvp.Value.SpecPath);
                return (kvp.Value.Name, Doc: JsonDocument.Parse(json), Success: true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Skipping {Service} - not available", kvp.Value.Name);
                return (kvp.Value.Name, Doc: (JsonDocument?)null, Success: false);
            }
        });

        var results = await Task.WhenAll(fetchTasks);
        foreach (var result in results)
            if (result.Success && result.Doc != null)
                backendSpecs.Add((result.Name, result.Doc));

        try
        {
            // 3. Build merged document
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();

                // Copy top-level properties (openapi, info, servers, security) — skip tags, paths, components
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Name is "paths" or "components" or "tags")
                        continue;
                    prop.WriteTo(writer);
                }

                // -- Merge tags --
                MergeTags(writer, root, backendSpecs);

                // -- Merge paths --
                writer.WritePropertyName("paths");
                writer.WriteStartObject();

                if (root.TryGetProperty("paths", out var gatewayPaths))
                    foreach (var path in gatewayPaths.EnumerateObject())
                        path.WriteTo(writer);

                foreach (var (_, svcDoc) in backendSpecs)
                    if (svcDoc.RootElement.TryGetProperty("paths", out var svcPaths))
                        foreach (var path in svcPaths.EnumerateObject())
                            path.WriteTo(writer);

                writer.WriteEndObject(); // end paths

                // -- Merge all components (schemas, parameters, requestBodies, responses, etc.) --
                MergeComponents(writer, root, backendSpecs);

                writer.WriteEndObject(); // end root
            }

            var bytes = buffer.ToArray();
            return Results.Bytes(bytes, "application/json");
        }
        finally
        {
            foreach (var (_, doc) in backendSpecs)
                doc.Dispose();
        }
    }

    /// <summary>
    /// Merges tags from gateway and all backend service specs.
    /// </summary>
    private static void MergeTags(
        Utf8JsonWriter writer,
        JsonElement gatewayRoot,
        List<(string Name, JsonDocument Doc)> backendSpecs)
    {
        var tagNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tags = new List<JsonElement>();

        // Collect gateway tags
        if (gatewayRoot.TryGetProperty("tags", out var gwTags))
            foreach (var tag in gwTags.EnumerateArray())
                if (tag.TryGetProperty("name", out var name) && tagNames.Add(name.GetString()!))
                    tags.Add(tag.Clone());

        // Collect backend tags
        foreach (var (_, svcDoc) in backendSpecs)
            if (svcDoc.RootElement.TryGetProperty("tags", out var svcTags))
                foreach (var tag in svcTags.EnumerateArray())
                    if (tag.TryGetProperty("name", out var name) && tagNames.Add(name.GetString()!))
                        tags.Add(tag.Clone());

        if (tags.Count > 0)
        {
            writer.WritePropertyName("tags");
            writer.WriteStartArray();
            foreach (var tag in tags)
                tag.WriteTo(writer);
            writer.WriteEndArray();
        }
    }

    /// <summary>
    /// Merges all component types (schemas, parameters, requestBodies, responses, headers, etc.)
    /// from gateway and backend services. Handles duplicate names by keeping the first occurrence.
    /// </summary>
    private static void MergeComponents(
        Utf8JsonWriter writer,
        JsonElement gatewayRoot,
        List<(string Name, JsonDocument Doc)> backendSpecs)
    {
        // Discover all component sections across gateway + backend
        var allSectionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var componentSources = new List<JsonElement>();

        if (gatewayRoot.TryGetProperty("components", out var gwComp))
        {
            componentSources.Add(gwComp);
            foreach (var section in gwComp.EnumerateObject())
                allSectionNames.Add(section.Name);
        }

        foreach (var (_, svcDoc) in backendSpecs)
            if (svcDoc.RootElement.TryGetProperty("components", out var svcComp))
            {
                componentSources.Add(svcComp);
                foreach (var section in svcComp.EnumerateObject())
                    allSectionNames.Add(section.Name);
            }

        if (allSectionNames.Count == 0)
            return;

        writer.WritePropertyName("components");
        writer.WriteStartObject();

        foreach (var sectionName in allSectionNames)
        {
            var writtenKeys = new HashSet<string>(StringComparer.Ordinal);

            writer.WritePropertyName(sectionName);
            writer.WriteStartObject();

            foreach (var comp in componentSources)
            {
                if (!comp.TryGetProperty(sectionName, out var section))
                    continue;

                foreach (var item in section.EnumerateObject())
                    // Skip duplicates — first occurrence wins
                    if (writtenKeys.Add(item.Name))
                    {
                        writer.WritePropertyName(item.Name);
                        item.Value.WriteTo(writer);
                    }
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject(); // end components
    }
}