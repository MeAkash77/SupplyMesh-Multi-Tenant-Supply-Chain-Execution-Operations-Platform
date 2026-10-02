using System.Diagnostics;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Serilog;
namespace SupplyMesh.Shared;

public static class Hosting
{
    /// <summary>OAuth2/OIDC JWT validation (Azure AD / Entra ID) + scope-based authorization policies.</summary>
    public static IServiceCollection AddSupplyMeshSecurity(this IServiceCollection s, IConfiguration cfg)
    {
        s.AddHttpContextAccessor();
        s.AddScoped<ITenantContext, HttpTenantContext>();
        s.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.Authority = cfg["Auth:Authority"];            // e.g. https://login.microsoftonline.com/{tenant}/v2.0
            o.Audience = cfg["Auth:Audience"];
            o.TokenValidationParameters.ValidateIssuer = true;
            o.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
        });
        s.AddAuthorization(o =>
        {
            foreach (var scope in new[] { "catalog.read", "catalog.write", "po.read", "po.write", "po.approve" })
                o.AddPolicy(scope, p => p.RequireAuthenticatedUser().RequireClaim("scp", scope));  // OAuth scope check
        });
        return s;
    }

    public static void UseSupplyMeshLogging(this WebApplicationBuilder b, string service) =>
        b.Host.UseSerilog((ctx, lc) => lc
            .ReadFrom.Configuration(ctx.Configuration).Enrich.FromLogContext()
            .Enrich.WithProperty("service", service)
            .WriteTo.Console(new Serilog.Formatting.Compact.RenderedCompactJsonFormatter())
            .WriteTo.Elasticsearch(new Serilog.Sinks.Elasticsearch.ElasticsearchSinkOptions(new Uri(ctx.Configuration["Elastic:Url"] ?? "http://elasticsearch:9200"))
            { IndexFormat = "supplymesh-logs-{0:yyyy.MM.dd}", AutoRegisterTemplate = true }));

    /// <summary>Correlation id + request timing + tenant tag: the three things you need when triaging a prod incident from logs.</summary>
    public static IApplicationBuilder UseObservability(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        var cid = ctx.Request.Headers["X-Correlation-Id"].FirstOrDefault() ?? Guid.NewGuid().ToString("N");
        ctx.Response.Headers["X-Correlation-Id"] = cid;
        var metrics = ctx.RequestServices.GetRequiredService<IMetrics>();
        var sw = Stopwatch.StartNew();
        using (Serilog.Context.LogContext.PushProperty("correlationId", cid))
        {
            try { await next(); }
            finally
            {
                sw.Stop();
                var route = ctx.GetEndpoint()?.DisplayName ?? "unknown";
                metrics.Timing("http.duration", sw.Elapsed.TotalMilliseconds, $"route:{route},status:{ctx.Response.StatusCode}");
                if (sw.ElapsedMilliseconds > 500) Log.Warning("SLOW_REQUEST {Route} {Ms}ms", route, sw.ElapsedMilliseconds);
            }
        }
    });
}
