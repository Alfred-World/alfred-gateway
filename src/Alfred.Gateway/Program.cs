using Alfred.Gateway.Configuration;
using Alfred.Gateway.Extensions;
using Alfred.Gateway.Middlewares;
using Microsoft.AspNetCore.HttpOverrides;
using StackExchange.Redis;

// ====================================================================================
// 1. LOAD ENVIRONMENT VARIABLES
// ====================================================================================
var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
DotEnvLoader.LoadForEnvironment(environment);

// Load and validate configuration from environment variables
var gatewayConfig = new GatewayConfiguration();
var mtlsConfig = new MtlsConfiguration();

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to listen on the specified hostname and port from environment
builder.WebHost.ConfigureKestrel((context, options) => { options.ListenAnyIP(gatewayConfig.AppPort); });

// Register GatewayConfiguration as singleton
builder.Services.AddSingleton(gatewayConfig);
builder.Services.AddSingleton(mtlsConfig);

// ====================================================================================
// 2. CONFIGURATION - Load file cấu hình riêng cho YARP
// ====================================================================================
// ====================================================================================
// 2. CONFIGURATION - Load file cấu hình riêng cho YARP (supports mTLS mode)
// ====================================================================================
if (mtlsConfig.Enabled)
{
    // Use mTLS configuration (HTTPS endpoints)
    // In Development, use localhost URLs; in Production, use Docker service names
    if (environment.Equals("Development", StringComparison.OrdinalIgnoreCase))
    {
        builder.Configuration.AddJsonFile("Configurations/yarp.mtls.Development.json", false, true);
        Console.WriteLine("✅ Loading YARP mTLS Development configuration (localhost HTTPS endpoints)");
    }
    else
    {
        builder.Configuration.AddJsonFile("Configurations/yarp.mtls.json", false, true);
        Console.WriteLine("✅ Loading YARP mTLS Production configuration (Docker HTTPS endpoints)");
    }
}
else
{
    // Use standard HTTP configuration
    builder.Configuration.AddJsonFile("Configurations/yarp.json", false, true);
    builder.Configuration.AddJsonFile($"Configurations/yarp.{environment}.json", true, true);
    Console.WriteLine("ℹ️ Loading YARP standard configuration (HTTP endpoints)");
}

// ====================================================================================
// 3. SERVICES REGISTRATION - Đăng ký các service cần thiết
// ====================================================================================

// Add CORS (cho phép Frontend gọi vào)
builder.Services.AddAlfredCors(gatewayConfig);

// Add Authentication & Authorization (kiểm tra JWT Token)
builder.Services.AddAlfredAuth(gatewayConfig);

// Add YARP Reverse Proxy & Rate Limiting (with mTLS support)
builder.Services.AddAlfredYarp(builder.Configuration, gatewayConfig, mtlsConfig);

// Add Health Checks (để monitoring biết service còn sống không)
builder.Services.AddHealthChecks();

// Add Scalar API documentation
builder.Services.AddAlfredScalar();

// Add Redis for Dynamic Authorization (optional - falls back gracefully)
var redisHost = Environment.GetEnvironmentVariable("REDIS_HOST");
if (!string.IsNullOrEmpty(redisHost))
{
    var redisPort = Environment.GetEnvironmentVariable("REDIS_PORT") ?? "6379";
    var redisPassword = Environment.GetEnvironmentVariable("REDIS_PASSWORD");

    var configOptions = new ConfigurationOptions
    {
        EndPoints = { $"{redisHost}:{redisPort}" },
        AbortOnConnectFail = false,
        ConnectRetry = 3,
        ConnectTimeout = 5000
    };

    if (!string.IsNullOrEmpty(redisPassword))
        configOptions.Password = redisPassword;

    try
    {
        var redis = ConnectionMultiplexer.Connect(configOptions);
        builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
        Console.WriteLine($"✅ Connected to Redis at {redisHost}:{redisPort}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"⚠️ Failed to connect to Redis: {ex.Message}. Dynamic authorization will be limited.");
    }
}

// Configure Forwarded Headers for reverse proxy support
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                               ForwardedHeaders.XForwardedProto |
                               ForwardedHeaders.XForwardedHost;
    // Clear KnownNetworks and KnownProxies for development
    // In production, you should configure these properly
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// ====================================================================================
// 3. BUILD APPLICATION
// ====================================================================================
var app = builder.Build();

// ====================================================================================
// 4. MIDDLEWARE PIPELINE (THỨ TỰ CỰC KỲ QUAN TRỌNG!)
// ====================================================================================

// 0. Forwarded Headers - PHẢI ĐẶT ĐẦU TIÊN để các middleware khác nhận đúng scheme/host
app.UseForwardedHeaders();

// 1. Global Exception Handler - Bắt lỗi toàn cục
app.UseGlobalExceptionHandler();

// 2. Scalar API reference - PHẢI ĐẶT TRƯỚC YARP để không bị proxy chặn
if (app.Environment.IsDevelopment()) app.UseAlfredScalar();

// 3. HTTPS Redirection (trong production nên bật)
if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();

// 4. CORS - Cho phép Cross-Origin requests
app.UseAlfredCors();

// 5. Authentication - Check "Mày là ai?" (verify token signature)
app.UseAuthentication();

// 6. Authorization - Check "Mày được làm gì?" (check permissions/roles)
app.UseAuthorization();

// 7. Dynamic Authorization - Check permissions from Redis cache
app.UseDynamicAuthorization();

// 8. Rate Limiting - Check "Mày spam à?" (prevent DDoS)
app.UseRateLimiter();

// ====================================================================================
// 5. ENDPOINTS
// ====================================================================================

// Health Check endpoint
app.MapHealthChecks("/health");

// YARP Reverse Proxy - Điều hướng requests tới các service backend
app.MapReverseProxy();

// ====================================================================================
// 6. RUN APPLICATION
// ====================================================================================
app.Logger.LogInformation("🚀 Alfred Gateway is starting...");
app.Logger.LogInformation("📍 Environment: {Environment}", gatewayConfig.Environment);
app.Logger.LogInformation("🌐 Listening on: http://{Hostname}:{Port}", gatewayConfig.AppHostname,
    gatewayConfig.AppPort);
app.Logger.LogInformation("🔒 Auth Authority: {Authority}", gatewayConfig.AuthAuthority);
app.Logger.LogInformation("🎯 Identity Service: {Url}", gatewayConfig.IdentityServiceUrl);
app.Logger.LogInformation("🎯 Core Service: {Url}", gatewayConfig.CoreServiceUrl);
app.Logger.LogInformation("🔐 mTLS Enabled: {Enabled}", mtlsConfig.Enabled);

app.Run();

app.Logger.LogInformation("✅ Alfred Gateway has been stopped.");