using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Alfred.Gateway.Configuration;
using Microsoft.AspNetCore.RateLimiting;

namespace Alfred.Gateway.Extensions;

/// <summary>
/// Extension methods for configuring YARP Reverse Proxy
/// </summary>
public static class YarpExtensions
{
    /// <summary>
    /// Adds YARP reverse proxy with rate limiting configuration and mTLS support
    /// </summary>
    public static IServiceCollection AddAlfredYarp(this IServiceCollection services, IConfiguration configuration,
        GatewayConfiguration config, MtlsConfiguration? mtlsConfig = null)
    {
        // 1. Add YARP Reverse Proxy with custom HttpClient configuration
        var proxyBuilder = services.AddReverseProxy()
            .LoadFromConfig(configuration.GetSection("ReverseProxy"));

        // Configure HttpClient for mTLS if enabled
        if (mtlsConfig?.Enabled == true)
        {
            var clientCert = mtlsConfig.LoadClientCertificate();
            var caCert = mtlsConfig.LoadCaCertificate();

            proxyBuilder.ConfigureHttpClient((context, handler) =>
            {
                // Add client certificate for mTLS
                handler.SslOptions.ClientCertificates = new X509CertificateCollection { clientCert };

                // Configure server certificate validation
                handler.SslOptions.RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
                {
                    if (mtlsConfig.SkipServerCertValidation)
                    {
                        return true;
                    }

                    if (certificate == null)
                    {
                        return false;
                    }

                    // Build certificate chain with our CA
                    using var customChain = new X509Chain();
                    customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    customChain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                    customChain.ChainPolicy.ExtraStore.Add(caCert);

                    var cert2 = new X509Certificate2(certificate);
                    var isValid = customChain.Build(cert2);

                    if (!isValid)
                    {
                        return false;
                    }

                    // Verify the certificate chain contains our CA
                    var chainContainsCa = customChain.ChainElements
                        .Any(element => element.Certificate.Thumbprint == caCert.Thumbprint);

                    return chainContainsCa;
                };
            });

            Console.WriteLine("✅ YARP configured with mTLS client certificate");
        }

        // 2. Add Rate Limiting (Chống spam request)
        var window = TimeSpan.FromMinutes(config.RateLimitWindowMinutes);
        var permitLimit = config.RateLimitPermitLimit;
        var queueLimit = config.RateLimitQueueLimit;

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Fixed Window Limiter - Giới hạn số request trong 1 khoảng thời gian cố định
            options.AddFixedWindowLimiter("fixed-window", opt =>
            {
                opt.Window = window;
                opt.PermitLimit = permitLimit;
                opt.QueueLimit = queueLimit;
                opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });

            // Global Limiter - Áp dụng cho tất cả endpoint
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.User.Identity?.Name ?? httpContext.Request.Headers.Host.ToString(),
                    partition => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = permitLimit,
                        QueueLimit = queueLimit,
                        Window = window
                    }));
        });

        return services;
    }
}