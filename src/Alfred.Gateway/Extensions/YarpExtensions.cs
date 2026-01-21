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
    /// Adds YARP reverse proxy with rate limiting configuration
    /// </summary>
    public static IServiceCollection AddAlfredYarp(this IServiceCollection services, IConfiguration configuration,
        GatewayConfiguration config)
    {
        // 1. Add YARP Reverse Proxy
        services.AddReverseProxy()
            .LoadFromConfig(configuration.GetSection("ReverseProxy"));

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