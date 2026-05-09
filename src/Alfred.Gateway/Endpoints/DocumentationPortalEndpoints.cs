using System.Text;

using Alfred.Gateway.Configuration;
using Alfred.Gateway.Extensions;

namespace Alfred.Gateway.Endpoints;

public static class DocumentationPortalEndpoints
{
    public sealed record ServiceDocumentation(
        string RouteSegment,
        string DisplayName,
        string Description,
        string UpstreamSwaggerPattern)
    {
        public string DocsPath => $"/{RouteSegment}/docs";
        public string OpenApiRoutePattern => $"/{RouteSegment}/openapi/{{documentName}}.json";
        public string DefaultSpecPath => ResolveGatewaySpecPath("v1");

        public string ResolveGatewaySpecPath(string documentName)
        {
            return OpenApiRoutePattern.Replace("{documentName}", documentName, StringComparison.Ordinal);
        }

        public string ResolveUpstreamSpecPath(string documentName)
        {
            return UpstreamSwaggerPattern.Replace("{documentName}", documentName, StringComparison.Ordinal);
        }
    }

    public static IReadOnlyList<ServiceDocumentation> BuildServiceDocs(
        IReadOnlyList<DynamicProxyDefinition> proxies)
    {
        var docs = new List<ServiceDocumentation>();

        foreach (var proxy in proxies.OrderBy(p => p.Order))
        {
            if (proxy.Kind.Equals("identity", StringComparison.OrdinalIgnoreCase))
            {
                docs.Add(new ServiceDocumentation(
                    proxy.Slug,
                    "Identity Service",
                    "OAuth2, OpenID Connect, SSO sessions, accounts, permissions.",
                    $"/api/{proxy.Slug}/swagger/{{documentName}}/swagger.json"));
                continue;
            }

            var displayName = string.Join(" ", proxy.Slug
                .Split('-')
                .Select(w => w.Length > 0 ? char.ToUpperInvariant(w[0]) + w[1..] : w))
                + " Service";

            docs.Add(new ServiceDocumentation(
                proxy.Slug,
                displayName,
                string.Empty,
                $"/api/{proxy.Slug}/swagger/{{documentName}}/swagger.json"));
        }

        return docs;
    }

    public static WebApplication MapDocumentationPortal(this WebApplication app)
    {
        var gatewayConfig = app.Services.GetRequiredService<GatewayConfiguration>();
        var dynamicProxies = app.Services.GetRequiredService<IReadOnlyList<DynamicProxyDefinition>>();
        var serviceDocs = BuildServiceDocs(dynamicProxies);

        foreach (var service in serviceDocs)
        {
            app.MapGet($"/{service.RouteSegment}/openapi/{{documentName}}.json", (string documentName) =>
                    Results.Redirect(service.ResolveUpstreamSpecPath(documentName), false))
                .ExcludeFromDescription();
        }

        app.MapGet("/docs", () =>
                Results.Content(BuildDocsPortalHtml(gatewayConfig, serviceDocs), "text/html; charset=utf-8"))
            .ExcludeFromDescription();

        return app;
    }

    private static string BuildDocsPortalHtml(
        GatewayConfiguration gatewayConfig,
        IReadOnlyList<ServiceDocumentation> services)
    {
        var cards = new StringBuilder();

        foreach (var service in services)
        {
            cards.Append($$"""
                <article class="card">
                  <div class="eyebrow">{{service.RouteSegment}}</div>
                  <h2>{{service.DisplayName}}</h2>
                  <p>{{service.Description}}</p>
                  <div class="actions">
                    <a class="primary" href="{{service.DocsPath}}">Open Scalar</a>
                    <a class="secondary" href="{{service.DefaultSpecPath}}">Open OpenAPI JSON</a>
                  </div>
                  <code>{{service.DocsPath}}</code>
                </article>
                """);
        }

        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1" />
              <title>Alfred Gateway Docs</title>
              <style>
                :root {
                  color-scheme: dark;
                  --bg: #0b1020;
                  --bg-noise: rgba(139, 92, 246, 0.12);
                  --surface: rgba(15, 23, 42, 0.78);
                  --surface-strong: rgba(17, 24, 39, 0.96);
                  --surface-border: rgba(148, 163, 184, 0.16);
                  --text: #f8fafc;
                  --muted: #94a3b8;
                  --muted-strong: #cbd5e1;
                  --primary: #8b5cf6;
                  --primary-strong: #7c3aed;
                  --secondary-bg: rgba(148, 163, 184, 0.1);
                  --secondary-border: rgba(148, 163, 184, 0.18);
                  --shadow: 0 24px 60px rgba(2, 6, 23, 0.45);
                  --radius-lg: 24px;
                  --radius-md: 18px;
                }
                * { box-sizing: border-box; }
                body {
                  margin: 0;
                  font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
                  color: var(--text);
                  background:
                    radial-gradient(circle at top left, rgba(139, 92, 246, 0.18), transparent 26%),
                    radial-gradient(circle at right, rgba(59, 130, 246, 0.14), transparent 24%),
                    linear-gradient(180deg, #0a0f1c 0%, var(--bg) 100%);
                  min-height: 100vh;
                }
                .shell {
                  min-height: 100vh;
                }
                .topbar {
                  position: sticky;
                  top: 0;
                  z-index: 10;
                  display: flex;
                  align-items: center;
                  justify-content: space-between;
                  gap: 20px;
                  min-height: 66px;
                  padding: 0 24px;
                  border-bottom: 1px solid var(--surface-border);
                  background: rgba(10, 15, 28, 0.84);
                  backdrop-filter: blur(14px);
                }
                .brand {
                  display: flex;
                  align-items: center;
                  gap: 12px;
                  font-size: 14px;
                  font-weight: 700;
                  letter-spacing: 0.01em;
                }
                .brand-mark {
                  display: inline-flex;
                  width: 28px;
                  height: 28px;
                  align-items: center;
                  justify-content: center;
                  border-radius: 9px;
                  background: linear-gradient(135deg, #8b5cf6 0%, #6366f1 100%);
                  box-shadow: 0 12px 28px rgba(124, 58, 237, 0.4);
                  font-size: 12px;
                  font-weight: 800;
                }
                .topbar-actions {
                  display: flex;
                  flex-wrap: wrap;
                  align-items: center;
                  justify-content: flex-end;
                  gap: 10px;
                }
                .topbar-chip {
                  display: inline-flex;
                  align-items: center;
                  min-height: 34px;
                  padding: 0 12px;
                  border: 1px solid var(--secondary-border);
                  border-radius: 999px;
                  color: var(--muted-strong);
                  background: rgba(15, 23, 42, 0.72);
                  font-size: 12px;
                }
                main {
                  max-width: 1240px;
                  margin: 0 auto;
                  padding: 40px 24px 80px;
                }
                .hero {
                  background:
                    linear-gradient(135deg, rgba(139, 92, 246, 0.14), transparent 40%),
                    linear-gradient(180deg, rgba(15, 23, 42, 0.94), rgba(15, 23, 42, 0.84));
                  backdrop-filter: blur(18px);
                  border: 1px solid var(--surface-border);
                  border-radius: var(--radius-lg);
                  box-shadow: var(--shadow);
                  padding: 34px;
                  margin-bottom: 28px;
                }
                .hero-kicker {
                  display: inline-flex;
                  align-items: center;
                  padding: 8px 12px;
                  border-radius: 999px;
                  background: rgba(139, 92, 246, 0.14);
                  color: #d8b4fe;
                  font-size: 12px;
                  font-weight: 700;
                  letter-spacing: 0.08em;
                  text-transform: uppercase;
                }
                h1 {
                  margin: 18px 0 12px;
                  font-size: clamp(32px, 4vw, 52px);
                  line-height: 0.95;
                  letter-spacing: -0.04em;
                }
                .hero p {
                  max-width: 760px;
                  margin: 0;
                  font-size: 16px;
                  line-height: 1.65;
                  color: var(--muted);
                }
                .meta {
                  margin-top: 18px;
                  font-size: 13px;
                  color: var(--muted);
                }
                .grid {
                  display: grid;
                  grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
                  gap: 20px;
                }
                .card {
                  position: relative;
                  overflow: hidden;
                  background: linear-gradient(180deg, rgba(15, 23, 42, 0.94), rgba(15, 23, 42, 0.84));
                  border: 1px solid var(--surface-border);
                  border-radius: var(--radius-md);
                  box-shadow: var(--shadow);
                  padding: 24px;
                }
                .card::before {
                  content: "";
                  position: absolute;
                  inset: 0 auto auto 0;
                  width: 100%;
                  height: 1px;
                  background: linear-gradient(90deg, rgba(139, 92, 246, 0.64), transparent 70%);
                }
                .eyebrow {
                  font-size: 12px;
                  font-weight: 800;
                  letter-spacing: 0.08em;
                  text-transform: uppercase;
                  color: #c4b5fd;
                }
                h2 {
                  margin: 12px 0 10px;
                  font-size: 28px;
                  letter-spacing: -0.03em;
                }
                .card p {
                  margin: 0 0 20px;
                  color: var(--muted);
                  line-height: 1.6;
                  min-height: 76px;
                }
                .actions {
                  display: flex;
                  flex-wrap: wrap;
                  gap: 12px;
                  margin-bottom: 14px;
                }
                a {
                  text-decoration: none;
                }
                .primary,
                .secondary {
                  display: inline-flex;
                  align-items: center;
                  justify-content: center;
                  min-height: 44px;
                  padding: 0 16px;
                  border-radius: 999px;
                  font-weight: 700;
                  font-size: 14px;
                  transition: transform 150ms ease, background 150ms ease, border-color 150ms ease;
                }
                .primary {
                  background: var(--primary);
                  color: white;
                }
                .primary:hover {
                  background: var(--primary-strong);
                  transform: translateY(-1px);
                }
                .secondary {
                  background: var(--secondary-bg);
                  border: 1px solid var(--secondary-border);
                  color: var(--muted-strong);
                }
                .secondary:hover {
                  background: rgba(148, 163, 184, 0.16);
                  transform: translateY(-1px);
                }
                code {
                  display: inline-block;
                  padding: 8px 10px;
                  border-radius: 12px;
                  background: rgba(2, 6, 23, 0.48);
                  border: 1px solid rgba(148, 163, 184, 0.12);
                  color: var(--muted-strong);
                  font-size: 13px;
                  word-break: break-all;
                }
                @media (max-width: 640px) {
                  .topbar {
                    padding: 12px 16px;
                    align-items: flex-start;
                    flex-direction: column;
                  }
                  .topbar-actions {
                    justify-content: flex-start;
                  }
                  main { padding: 24px 16px 48px; }
                  .hero { padding: 24px; border-radius: 20px; }
                  .card { border-radius: 18px; }
                }
              </style>
            </head>
            <body>
              <div class="shell">
                <header class="topbar">
                  <div class="brand">
                    <span class="brand-mark">S</span>
                    <span>Alfred Gateway API Reference</span>
                  </div>
                  <div class="topbar-actions">
                    <span class="topbar-chip">Environment: {{gatewayConfig.Environment}}</span>
                    <span class="topbar-chip">Services: {{services.Count}}</span>
                  </div>
                </header>
                <main>
                  <section class="hero">
                    <span class="hero-kicker">Gateway Documentation Portal</span>
                    <h1>Choose a service, not a merged schema.</h1>
                    <p>
                      This index follows the same focused visual language as Scalar, but keeps each downstream API isolated.
                      Open the service you are actively developing so schemas, request samples, and auth flows stay scoped.
                    </p>
                    <div class="meta">Each card opens a dedicated Scalar page backed by that service's own OpenAPI document.</div>
                  </section>
                  <section class="grid">{{cards}}</section>
                </main>
              </div>
            </body>
            </html>
            """;
    }
}