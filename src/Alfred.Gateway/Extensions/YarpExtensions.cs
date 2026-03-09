using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Alfred.Gateway.Configuration;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;

namespace Alfred.Gateway.Extensions;

public static class YarpExtensions
{
    /// <summary>
    /// Registers YARP reverse proxy, injects cluster addresses from env vars, and configures rate limiting.
    /// Accepts <see cref="IConfigurationManager"/> so cluster addresses can be injected before YARP reads them.
    /// </summary>
    public static IServiceCollection AddAlfredYarp(
        this IServiceCollection services,
        IConfigurationManager configuration,
        GatewayConfiguration config,
        MtlsConfiguration? mtlsConfig = null)
    {
        // ── 1. Inject cluster addresses from env vars ──────────────────────────
        // Done here (not Program.cs) so YARP's LoadFromConfig picks them up correctly.
        configuration.AddInMemoryCollection(BuildClusterAddresses(config, mtlsConfig));

        var mode = mtlsConfig?.Enabled == true ? "mTLS" : "HTTP";
        Console.WriteLine($"🔗 [{mode}] identity  → {(mtlsConfig?.Enabled == true ? config.IdentityServiceMtlsUrl : config.IdentityServiceUrl)}");
        Console.WriteLine($"🔗 [{mode}] core      → {(mtlsConfig?.Enabled == true ? config.CoreServiceMtlsUrl : config.CoreServiceUrl)}");
        Console.WriteLine($"🔗 [{mode}] notif     → {config.NotificationServiceUrl}");

        // ── 2. Register YARP ───────────────────────────────────────────────────
        var proxyBuilder = services
            .AddReverseProxy()
            .LoadFromConfig(configuration.GetSection("ReverseProxy"));

        if (mtlsConfig?.Enabled == true)
            proxyBuilder.ConfigureMtlsHttpClient(mtlsConfig);

        // ── 3. Rate limiting ───────────────────────────────────────────────────
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

        return services;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static Dictionary<string, string?> BuildClusterAddresses(
        GatewayConfiguration config, MtlsConfiguration? mtls)
    {
        var useMtls = mtls?.Enabled == true;
        return new()
        {
            ["ReverseProxy:Clusters:identity-cluster:Destinations:destination1:Address"] =
                useMtls ? config.IdentityServiceMtlsUrl : config.IdentityServiceUrl,
            ["ReverseProxy:Clusters:core-cluster:Destinations:destination1:Address"] =
                useMtls ? config.CoreServiceMtlsUrl : config.CoreServiceUrl,
            ["ReverseProxy:Clusters:notification-cluster:Destinations:destination1:Address"] =
                config.NotificationServiceUrl
        };
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

        Console.WriteLine("✅ YARP configured with mTLS client certificate");
        return proxyBuilder;
    }
}
