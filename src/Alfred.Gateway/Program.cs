using Alfred.Gateway.Configuration;
using Alfred.Gateway.Extensions;
using Alfred.Gateway.Middlewares;

// ====================================================================================
// 1. LOAD ENVIRONMENT VARIABLES
// ====================================================================================
var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
DotEnvLoader.LoadForEnvironment(environment);

// Load and validate configuration from environment variables
var gatewayConfig = new GatewayConfiguration();

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to listen on the specified hostname and port from environment
builder.WebHost.ConfigureKestrel((context, options) => 
{ 
    options.ListenAnyIP(gatewayConfig.AppPort); 
});

// Register GatewayConfiguration as singleton
builder.Services.AddSingleton(gatewayConfig);

// ====================================================================================
// 2. CONFIGURATION - Load file cấu hình riêng cho YARP
// ====================================================================================
builder.Configuration.AddJsonFile(
    "Configurations/yarp.json", 
    optional: false, 
    reloadOnChange: true);

// ====================================================================================
// 3. SERVICES REGISTRATION - Đăng ký các service cần thiết
// ====================================================================================

// Add CORS (cho phép Frontend gọi vào)
builder.Services.AddAlfredCors(gatewayConfig);

// Add Authentication & Authorization (kiểm tra JWT Token)
builder.Services.AddAlfredAuth(gatewayConfig);

// Add YARP Reverse Proxy & Rate Limiting
builder.Services.AddAlfredYarp(builder.Configuration, gatewayConfig);

// Add Health Checks (để monitoring biết service còn sống không)
builder.Services.AddHealthChecks();

// Add Swagger with API aggregation
builder.Services.AddAlfredSwagger();

// ====================================================================================
// 3. BUILD APPLICATION
// ====================================================================================
var app = builder.Build();

// ====================================================================================
// 4. MIDDLEWARE PIPELINE (THỨ TỰ CỰC KỲ QUAN TRỌNG!)
// ====================================================================================

// 1. Global Exception Handler - Bắt lỗi toàn cục
app.UseGlobalExceptionHandler();

// 2. Swagger - PHẢI ĐẶT TRƯỚC YARP để không bị proxy chặn
app.UseAlfredSwagger(builder.Configuration);

// 3. HTTPS Redirection (trong production nên bật)
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// 4. CORS - Cho phép Cross-Origin requests
app.UseAlfredCors();

// 5. Authentication - Check "Mày là ai?" (verify token signature)
app.UseAuthentication();

// 6. Authorization - Check "Mày được làm gì?" (check permissions/roles)
app.UseAuthorization();

// 7. Rate Limiting - Check "Mày spam à?" (prevent DDoS)
app.UseRateLimiter();

// ====================================================================================
// 5. ENDPOINTS
// ====================================================================================

// Health Check endpoint
app.MapHealthChecks("/health");

// Gateway Info endpoint (cho biết gateway đang chạy)
app.MapGet("/", () => new
{
    service = "Alfred API Gateway",
    version = "1.0.0",
    status = "running",
    environment = gatewayConfig.Environment,
    port = gatewayConfig.AppPort,
    timestamp = DateTime.UtcNow
});

// YARP Reverse Proxy - Điều hướng requests tới các service backend
app.MapReverseProxy();

// ====================================================================================
// 6. RUN APPLICATION
// ====================================================================================
app.Logger.LogInformation("🚀 Alfred Gateway is starting...");
app.Logger.LogInformation("📍 Environment: {Environment}", gatewayConfig.Environment);
app.Logger.LogInformation("🌐 Listening on: http://{Hostname}:{Port}", gatewayConfig.AppHostname, gatewayConfig.AppPort);
app.Logger.LogInformation("🔒 Auth Authority: {Authority}", gatewayConfig.AuthAuthority);
app.Logger.LogInformation("🎯 Identity Service: {Url}", gatewayConfig.IdentityServiceUrl);
app.Logger.LogInformation("🎯 Core Service: {Url}", gatewayConfig.CoreServiceUrl);

app.Run();

app.Logger.LogInformation("✅ Alfred Gateway has been stopped.");
