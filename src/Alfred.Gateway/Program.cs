using Alfred.Gateway.Configuration;
using Alfred.Gateway.Extensions;
using Alfred.Gateway.Middlewares;
using Microsoft.AspNetCore.HttpOverrides;

// ── 1. Environment & configuration ────────────────────────────────────────────
var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
DotEnvLoader.LoadForEnvironment(environment);

var gatewayConfig = new GatewayConfiguration();
var mtlsConfig = new MtlsConfiguration();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel((_, opts) => opts.ListenAnyIP(gatewayConfig.AppPort));

// Single YARP config file — all routes, all services.
// Cluster addresses are injected at runtime (see AddAlfredYarp).
builder.Configuration.AddJsonFile("Configurations/yarp.json", optional: false, reloadOnChange: true);

// ── 2. Services ────────────────────────────────────────────────────────────────
builder.Services
    .AddSingleton(gatewayConfig)
    .AddSingleton(mtlsConfig)
    .AddAlfredCors(gatewayConfig)
    .AddAlfredAuth(gatewayConfig)
    .AddAlfredYarp(builder.Configuration, gatewayConfig, mtlsConfig)
    .AddAlfredRedis()
    .AddAlfredScalar();

builder.Services.AddHealthChecks();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                             | ForwardedHeaders.XForwardedProto
                             | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// ── 3. Middleware pipeline ─────────────────────────────────────────────────────
var app = builder.Build();

app.UseForwardedHeaders();
app.UseGlobalExceptionHandler();

if (app.Environment.IsDevelopment()) app.UseAlfredScalar();
if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();

app.UseAlfredCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// ── 4. Endpoints ───────────────────────────────────────────────────────────────
app.MapHealthChecks("/health");
app.MapReverseProxy();

// ── 5. Run ─────────────────────────────────────────────────────────────────────
app.Logger.LogInformation("🚀 Alfred Gateway | env={Env} | port={Port} | mTLS={Mtls}",
    gatewayConfig.Environment, gatewayConfig.AppPort, mtlsConfig.Enabled);

app.Run();
