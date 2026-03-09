using StackExchange.Redis;

namespace Alfred.Gateway.Extensions;

public static class RedisExtensions
{
    public static IServiceCollection AddAlfredRedis(this IServiceCollection services)
    {
        var host = Environment.GetEnvironmentVariable("REDIS_HOST");
        if (string.IsNullOrEmpty(host))
        {
            Console.WriteLine("ℹ️  REDIS_HOST not set — dynamic authorization disabled.");
            return services;
        }

        var port = Environment.GetEnvironmentVariable("REDIS_PORT") ?? "6379";
        var password = Environment.GetEnvironmentVariable("REDIS_PASSWORD");

        var options = new ConfigurationOptions
        {
            EndPoints = { $"{host}:{port}" },
            AbortOnConnectFail = false,
            ConnectRetry = 3,
            ConnectTimeout = 5000
        };

        if (!string.IsNullOrEmpty(password))
            options.Password = password;

        try
        {
            var redis = ConnectionMultiplexer.Connect(options);
            services.AddSingleton<IConnectionMultiplexer>(redis);
            Console.WriteLine($"✅ Redis connected at {host}:{port}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️  Redis connection failed: {ex.Message}. Dynamic authorization disabled.");
        }

        return services;
    }
}
