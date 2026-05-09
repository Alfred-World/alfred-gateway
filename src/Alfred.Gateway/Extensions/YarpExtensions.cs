using System.Collections;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.RateLimiting;
using Alfred.Gateway.Configuration;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;

namespace Alfred.Gateway.Extensions;

public static class YarpExtensions
{
    private const string DynamicProxyEnvPrefix = "DYNAMIC_PROXY__";
    private const string DefaultAuthorizationPolicy = "Authenticated";
    private const string DefaultApiPath = "/{0}/v{{version}}/{{**remainder}}";
    private const string DefaultSwaggerTransformPath = "/swagger/{**remainder}";
    private const string DefaultClusterHealthPath = "/health";
    private const string IdentityProxyKind = "identity";
    private const string ApiProxyKind = "api";

    /// <summary>
    /// Registers YARP reverse proxy, builds routes/clusters from env vars, and configures rate limiting.
    /// Accepts <see cref="IConfigurationManager"/> so dynamic entries are available before YARP reads config.
    /// </summary>
    public static IServiceCollection AddAlfredYarp(
        this IServiceCollection services,
        IConfigurationManager configuration,
        GatewayConfiguration config,
        MtlsConfiguration? mtlsConfig = null)
    {
        var dynamicProxies = LoadDynamicProxyDefinitions(mtlsConfig);
        if (dynamicProxies.Count == 0)
            throw new InvalidOperationException(
                "No dynamic proxy definitions were configured. Add at least DYNAMIC_PROXY__IDENTITY to .env.");

        LogConfiguredClusters(dynamicProxies);
        services.AddSingleton<IReadOnlyList<DynamicProxyDefinition>>(dynamicProxies);
        RegisterReverseProxy(services, configuration, config, mtlsConfig, dynamicProxies);
        AddRateLimiting(services, config);

        return services;
    }

    private static void LogConfiguredClusters(IReadOnlyCollection<DynamicProxyDefinition> dynamicProxies)
    {
        foreach (var proxy in dynamicProxies)
            Console.WriteLine($"[proxy] {proxy.Key} ({proxy.Kind}) -> {proxy.Address}");
    }

    private static void RegisterReverseProxy(
        IServiceCollection services,
        IConfiguration configuration,
        GatewayConfiguration config,
        MtlsConfiguration? mtlsConfig,
        IReadOnlyCollection<DynamicProxyDefinition> dynamicProxies)
    {
        var reverseProxyConfiguration = BuildReverseProxyConfiguration(
            configuration,
            dynamicProxies,
            config.HealthCheckIntervalSeconds,
            config.HealthCheckTimeoutSeconds);

        var proxyBuilder = services
            .AddReverseProxy()
            .LoadFromConfig(reverseProxyConfiguration.GetSection("ReverseProxy"));

        if (mtlsConfig?.Enabled == true)
            proxyBuilder.ConfigureMtlsHttpClient(mtlsConfig);
    }

    private static void AddRateLimiting(IServiceCollection services, GatewayConfiguration config)
    {
        if (!config.RateLimitEnabled)
            return;

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            var window = TimeSpan.FromMinutes(config.RateLimitWindowMinutes);

            options.AddFixedWindowLimiter("fixed-window", opt =>
            {
                opt.Window = window;
                opt.PermitLimit = config.RateLimitPermitLimit;
                opt.QueueLimit = config.RateLimitQueueLimit;
                opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ctx.User.Identity?.Name ?? ctx.Request.Headers.Host.ToString(),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = config.RateLimitPermitLimit,
                        QueueLimit = config.RateLimitQueueLimit,
                        Window = window
                    }));
        });
    }

    private static IConfiguration BuildReverseProxyConfiguration(
        IConfiguration configuration,
        IReadOnlyCollection<DynamicProxyDefinition> dynamicProxies,
        int healthCheckIntervalSeconds,
        int healthCheckTimeoutSeconds)
    {
        var entries = configuration
            .GetSection("ReverseProxy")
            .AsEnumerable()
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Value));

        var filteredConfiguration = new ConfigurationManager();
        filteredConfiguration.AddInMemoryCollection(entries.ToDictionary(entry => entry.Key, entry => entry.Value));
        filteredConfiguration.AddInMemoryCollection(
            BuildDynamicProxyEntries(dynamicProxies, healthCheckIntervalSeconds, healthCheckTimeoutSeconds));

        return filteredConfiguration;
    }

    private static Dictionary<string, string?> BuildDynamicProxyEntries(
        IReadOnlyCollection<DynamicProxyDefinition> dynamicProxies,
        int healthCheckIntervalSeconds,
        int healthCheckTimeoutSeconds)
    {
        var entries = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var healthCheckInterval = TimeSpan.FromSeconds(healthCheckIntervalSeconds).ToString("c");
        var healthCheckTimeout = TimeSpan.FromSeconds(healthCheckTimeoutSeconds).ToString("c");

        foreach (var proxy in dynamicProxies)
        {
            BuildClusterEntries(entries, proxy, healthCheckInterval, healthCheckTimeout);

            if (proxy.Kind.Equals(IdentityProxyKind, StringComparison.OrdinalIgnoreCase))
            {
                BuildIdentityRouteEntries(entries, proxy);
                continue;
            }

            BuildApiRouteEntries(entries, proxy);
        }

        return entries;
    }

    private static void BuildClusterEntries(
        IDictionary<string, string?> entries,
        DynamicProxyDefinition proxy,
        string healthCheckInterval,
        string healthCheckTimeout)
    {
        var clusterPrefix = $"ReverseProxy:Clusters:{proxy.ClusterId}";
        entries[$"{clusterPrefix}:Destinations:destination1:Address"] = proxy.Address;
        entries[$"{clusterPrefix}:HealthCheck:Active:Enabled"] = bool.TrueString;
        entries[$"{clusterPrefix}:HealthCheck:Active:Interval"] = healthCheckInterval;
        entries[$"{clusterPrefix}:HealthCheck:Active:Timeout"] = healthCheckTimeout;
        entries[$"{clusterPrefix}:HealthCheck:Active:Policy"] = "ConsecutiveFailures";
        entries[$"{clusterPrefix}:HealthCheck:Active:Path"] = DefaultClusterHealthPath;
    }

    private static void BuildApiRouteEntries(IDictionary<string, string?> entries, DynamicProxyDefinition proxy)
    {
        var routePrefix = $"ReverseProxy:Routes:env-{proxy.Slug}";

        entries[$"{routePrefix}-api-route:ClusterId"] = proxy.ClusterId;
        entries[$"{routePrefix}-api-route:AuthorizationPolicy"] = proxy.AuthorizationPolicy;
        entries[$"{routePrefix}-api-route:Match:Path"] = proxy.ApiPath;
        entries[$"{routePrefix}-api-route:Order"] = proxy.Order.ToString();

        if (!string.IsNullOrWhiteSpace(proxy.Host))
        {
            entries[$"{routePrefix}-api-route:Match:Headers:0:Name"] = "X-Forwarded-Host";
            entries[$"{routePrefix}-api-route:Match:Headers:0:Values:0"] = proxy.Host;
            entries[$"{routePrefix}-api-route:Match:Headers:0:Mode"] = "ExactHeader";
        }

        entries[$"{routePrefix}-swagger-route:ClusterId"] = proxy.ClusterId;
        entries[$"{routePrefix}-swagger-route:AuthorizationPolicy"] = "AllowAnonymous";
        entries[$"{routePrefix}-swagger-route:Match:Path"] = proxy.SwaggerPath;
        entries[$"{routePrefix}-swagger-route:Transforms:0:PathPattern"] = proxy.SwaggerTransformPath;
        entries[$"{routePrefix}-swagger-route:Order"] = "1";

        entries[$"{routePrefix}-health-route:ClusterId"] = proxy.ClusterId;
        entries[$"{routePrefix}-health-route:AuthorizationPolicy"] = "AllowAnonymous";
        entries[$"{routePrefix}-health-route:Match:Path"] = proxy.HealthPath;
        entries[$"{routePrefix}-health-route:Transforms:0:PathPattern"] = DefaultClusterHealthPath;
    }

    private static void BuildIdentityRouteEntries(IDictionary<string, string?> entries, DynamicProxyDefinition proxy)
    {
        var clusterId = proxy.ClusterId;
        var slug = proxy.Slug;

        entries["ReverseProxy:Routes:identity-swagger-route:ClusterId"] = clusterId;
        entries["ReverseProxy:Routes:identity-swagger-route:AuthorizationPolicy"] = "AllowAnonymous";
        entries["ReverseProxy:Routes:identity-swagger-route:Match:Path"] =
            string.IsNullOrWhiteSpace(proxy.SwaggerPath)
                ? $"/api/{slug}/swagger/{{**remainder}}"
                : proxy.SwaggerPath;
        entries["ReverseProxy:Routes:identity-swagger-route:Transforms:0:PathPattern"] = proxy.SwaggerTransformPath;

        entries["ReverseProxy:Routes:identity-auth-route:ClusterId"] = clusterId;
        entries["ReverseProxy:Routes:identity-auth-route:AuthorizationPolicy"] = "AllowAnonymous";
        entries["ReverseProxy:Routes:identity-auth-route:Match:Path"] = "/identity/v{version}/auth/{**remainder}";
        entries["ReverseProxy:Routes:identity-auth-route:Transforms:0:X-Forwarded"] = "Set";
        entries["ReverseProxy:Routes:identity-auth-route:Order"] = "1";

        entries["ReverseProxy:Routes:identity-connect-route:ClusterId"] = clusterId;
        entries["ReverseProxy:Routes:identity-connect-route:AuthorizationPolicy"] = "AllowAnonymous";
        entries["ReverseProxy:Routes:identity-connect-route:Match:Path"] = "/connect/{**remainder}";
        entries["ReverseProxy:Routes:identity-connect-route:Transforms:0:X-Forwarded"] = "Set";
        entries["ReverseProxy:Routes:identity-connect-route:Order"] = "1";

        entries["ReverseProxy:Routes:identity-wellknown-route:ClusterId"] = clusterId;
        entries["ReverseProxy:Routes:identity-wellknown-route:AuthorizationPolicy"] = "AllowAnonymous";
        entries["ReverseProxy:Routes:identity-wellknown-route:Match:Path"] = "/.well-known/{**remainder}";
        entries["ReverseProxy:Routes:identity-wellknown-route:Order"] = "1";

        entries["ReverseProxy:Routes:identity-applications-route:ClusterId"] = clusterId;
        entries["ReverseProxy:Routes:identity-applications-route:AuthorizationPolicy"] = "Authenticated";
        entries["ReverseProxy:Routes:identity-applications-route:Match:Path"] = "/identity/v{version}/applications/{**remainder}";
        entries["ReverseProxy:Routes:identity-applications-route:Order"] = "1";

        entries["ReverseProxy:Routes:identity-roles-route:ClusterId"] = clusterId;
        entries["ReverseProxy:Routes:identity-roles-route:AuthorizationPolicy"] = "Authenticated";
        entries["ReverseProxy:Routes:identity-roles-route:Match:Path"] = "/identity/v{version}/roles/{**remainder}";
        entries["ReverseProxy:Routes:identity-roles-route:Order"] = "1";

        entries["ReverseProxy:Routes:identity-permissions-route:ClusterId"] = clusterId;
        entries["ReverseProxy:Routes:identity-permissions-route:AuthorizationPolicy"] = "Authenticated";
        entries["ReverseProxy:Routes:identity-permissions-route:Match:Path"] = "/identity/v{version}/permissions/{**remainder}";
        entries["ReverseProxy:Routes:identity-permissions-route:Order"] = "1";

        entries["ReverseProxy:Routes:identity-users-route:ClusterId"] = clusterId;
        entries["ReverseProxy:Routes:identity-users-route:AuthorizationPolicy"] = "Authenticated";
        entries["ReverseProxy:Routes:identity-users-route:Match:Path"] = "/identity/v{version}/mgmt/users/{**remainder}";
        entries["ReverseProxy:Routes:identity-users-route:Order"] = "1";

        entries["ReverseProxy:Routes:identity-account-route:ClusterId"] = clusterId;
        entries["ReverseProxy:Routes:identity-account-route:AuthorizationPolicy"] = "Authenticated";
        entries["ReverseProxy:Routes:identity-account-route:Match:Path"] = "/identity/v{version}/account/{**remainder}";
        entries["ReverseProxy:Routes:identity-account-route:Order"] = "1";

        entries["ReverseProxy:Routes:identity-keys-route:ClusterId"] = clusterId;
        entries["ReverseProxy:Routes:identity-keys-route:AuthorizationPolicy"] = "Authenticated";
        entries["ReverseProxy:Routes:identity-keys-route:Match:Path"] = "/identity/v{version}/keys/{**remainder}";
        entries["ReverseProxy:Routes:identity-keys-route:Order"] = "1";

        entries["ReverseProxy:Routes:identity-external-auth-route:ClusterId"] = clusterId;
        entries["ReverseProxy:Routes:identity-external-auth-route:AuthorizationPolicy"] = "AllowAnonymous";
        entries["ReverseProxy:Routes:identity-external-auth-route:Match:Path"] = "/identity/v{version}/external-auth/{**remainder}";
        entries["ReverseProxy:Routes:identity-external-auth-route:Order"] = "1";

        entries["ReverseProxy:Routes:identity-catchall-route:ClusterId"] = clusterId;
        entries["ReverseProxy:Routes:identity-catchall-route:AuthorizationPolicy"] = "Authenticated";
        entries["ReverseProxy:Routes:identity-catchall-route:Match:Path"] = "/identity/v{version}/{**remainder}";
        entries["ReverseProxy:Routes:identity-catchall-route:Order"] = "10";

        entries["ReverseProxy:Routes:identity-signalr-hub-route:ClusterId"] = clusterId;
        entries["ReverseProxy:Routes:identity-signalr-hub-route:AuthorizationPolicy"] = "AllowAnonymous";
        entries["ReverseProxy:Routes:identity-signalr-hub-route:Match:Path"] = "/hubs/{**remainder}";
        entries["ReverseProxy:Routes:identity-signalr-hub-route:Order"] = "1";

        entries["ReverseProxy:Routes:health-check-identity-route:ClusterId"] = clusterId;
        entries["ReverseProxy:Routes:health-check-identity-route:AuthorizationPolicy"] = "AllowAnonymous";
        entries["ReverseProxy:Routes:health-check-identity-route:Match:Path"] = $"/health/{slug}";
        entries["ReverseProxy:Routes:health-check-identity-route:Transforms:0:PathPattern"] = DefaultClusterHealthPath;
    }

    public static List<DynamicProxyDefinition> LoadDynamicProxyDefinitions(MtlsConfiguration? mtlsConfig = null)
    {
        var proxies = new List<DynamicProxyDefinition>();

        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is not string key
                || !key.StartsWith(DynamicProxyEnvPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            if (entry.Value is not string rawValue || string.IsNullOrWhiteSpace(rawValue))
                continue;

            var proxyKey = key[DynamicProxyEnvPrefix.Length..].Trim();
            if (string.IsNullOrWhiteSpace(proxyKey))
                continue;

            try
            {
                proxies.Add(ParseDynamicProxyDefinition(proxyKey, rawValue));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Invalid dynamic proxy env '{key}'. Expected JSON with at least 'address'. Value: '{rawValue}'.",
                    ex);
            }
        }

        return proxies.Count > 0 ? proxies : LoadLegacyProxyDefinitions(mtlsConfig);
    }

    private static List<DynamicProxyDefinition> LoadLegacyProxyDefinitions(MtlsConfiguration? mtlsConfig)
    {
        var useMtls = mtlsConfig?.Enabled == true;
        var proxies = new List<DynamicProxyDefinition>();

        AddLegacyProxy(
            proxies,
            key: "IDENTITY",
            kind: IdentityProxyKind,
            slug: "identity",
            address: GetLegacyAddress("IDENTITY_SERVICE_URL", "IDENTITY_SERVICE_MTLS_URL", useMtls),
            apiPath: FormatDefaultApiPath("identity"),
            swaggerPath: "/api/identity/swagger/{**remainder}",
            healthPath: "/health/identity",
            swaggerTransformPath: DefaultSwaggerTransformPath,
            order: 1);

        AddLegacyProxy(
            proxies,
            key: "CORE",
            kind: ApiProxyKind,
            slug: "core",
            address: GetLegacyAddress("CORE_SERVICE_URL", "CORE_SERVICE_MTLS_URL", useMtls),
            apiPath: FormatDefaultApiPath("core"),
            swaggerPath: "/api/core/swagger/{**remainder}",
            healthPath: "/health/core",
            swaggerTransformPath: DefaultSwaggerTransformPath,
            order: 20);

        AddLegacyProxy(
            proxies,
            key: "NOTIFICATION",
            kind: ApiProxyKind,
            slug: "notification",
            address: Environment.GetEnvironmentVariable("NOTIFICATION_SERVICE_URL"),
            apiPath: "/templates/{**remainder}",
            swaggerPath: "/api/notification/swagger/{**remainder}",
            healthPath: "/health/notification",
            swaggerTransformPath: "/{**remainder}",
            order: 30);

        return proxies;
    }

    private static void AddLegacyProxy(
        ICollection<DynamicProxyDefinition> proxies,
        string key,
        string kind,
        string slug,
        string? address,
        string apiPath,
        string swaggerPath,
        string healthPath,
        string swaggerTransformPath,
        int order)
    {
        if (string.IsNullOrWhiteSpace(address))
            return;

        proxies.Add(new DynamicProxyDefinition(
            key,
            kind,
            slug,
            address.Trim(),
            null,
            DefaultAuthorizationPolicy,
            apiPath,
            swaggerPath,
            healthPath,
            swaggerTransformPath,
            order,
            $"env-{slug}-cluster"));
    }

    private static string? GetLegacyAddress(string httpKey, string mtlsKey, bool useMtls)
    {
        var preferredKey = useMtls ? mtlsKey : httpKey;
        var fallbackKey = useMtls ? httpKey : mtlsKey;

        return Environment.GetEnvironmentVariable(preferredKey)
               ?? Environment.GetEnvironmentVariable(fallbackKey);
    }

    private static DynamicProxyDefinition ParseDynamicProxyDefinition(string proxyKey, string rawValue)
    {
        var settings = JsonSerializer.Deserialize<DynamicProxyEnvironmentSettings>(rawValue,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        if (settings == null || string.IsNullOrWhiteSpace(settings.Address))
            throw new InvalidOperationException("address is required");

        var slug = GetTrimmedOrDefault(settings.Slug, proxyKey.ToLowerInvariant().Replace('_', '-'));
        var kind = GetTrimmedOrDefault(settings.Kind, ApiProxyKind);

        return new DynamicProxyDefinition(
            proxyKey,
            kind,
            slug,
            settings.Address.Trim(),
            string.IsNullOrWhiteSpace(settings.Host) ? null : settings.Host.Trim(),
            GetTrimmedOrDefault(settings.AuthorizationPolicy, DefaultAuthorizationPolicy),
            GetTrimmedOrDefault(settings.ApiPath, FormatDefaultApiPath(slug)),
            GetTrimmedOrDefault(settings.SwaggerPath, $"/api/{slug}/swagger/{{**remainder}}"),
            GetTrimmedOrDefault(settings.HealthPath, $"/health/{slug}"),
            GetTrimmedOrDefault(settings.SwaggerTransformPath, DefaultSwaggerTransformPath),
            settings.Order ?? 10,
            $"env-{slug}-cluster");
    }

    private static string FormatDefaultApiPath(string slug)
    {
        return string.Format(DefaultApiPath, slug);
    }

    private static string GetTrimmedOrDefault(string? value, string defaultValue)
    {
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
    }

    private static IReverseProxyBuilder ConfigureMtlsHttpClient(
        this IReverseProxyBuilder proxyBuilder, MtlsConfiguration mtlsConfig)
    {
        var clientCert = mtlsConfig.LoadClientCertificate();
        var caCert = mtlsConfig.LoadCaCertificate();

        proxyBuilder.ConfigureHttpClient((_, handler) =>
        {
            handler.SslOptions.ClientCertificates = new X509CertificateCollection { clientCert };
            handler.SslOptions.RemoteCertificateValidationCallback =
                (_, certificate, _, _) =>
                {
                    if (mtlsConfig.SkipServerCertValidation) return true;
                    if (certificate == null) return false;

                    using var chain = new X509Chain();
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                    chain.ChainPolicy.ExtraStore.Add(caCert);

                    if (!chain.Build(new X509Certificate2(certificate))) return false;

                    return chain.ChainElements
                        .Any(e => e.Certificate.Thumbprint == caCert.Thumbprint);
                };
        });

        Console.WriteLine("YARP configured with mTLS client certificate");
        return proxyBuilder;
    }

    private sealed class DynamicProxyEnvironmentSettings
    {
        public string? Kind { get; init; }
        public string? Slug { get; init; }
        public string? Address { get; init; }
        public string? Host { get; init; }
        public string? AuthorizationPolicy { get; init; }
        public string? ApiPath { get; init; }
        public string? SwaggerPath { get; init; }
        public string? HealthPath { get; init; }
        public string? SwaggerTransformPath { get; init; }
        public int? Order { get; init; }
    }
}

public sealed record DynamicProxyDefinition(
    string Key,
    string Kind,
    string Slug,
    string Address,
    string? Host,
    string AuthorizationPolicy,
    string ApiPath,
    string SwaggerPath,
    string HealthPath,
    string SwaggerTransformPath,
    int Order,
    string ClusterId);
