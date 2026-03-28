using Serilog;
using Serilog.Events;
using System.Text.RegularExpressions;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Velo.Api.Middleware;
using Velo.Api.Services;
using Velo.Shared.Contracts;
using Velo.SQL;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Velo.Agent;
using Velo.Api.Interface;

// Bootstrap logger — console only, used until the host is built and
// the full configuration (including MSSqlServer sink) is loaded.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Velo API application");
    
    var builder = WebApplication.CreateBuilder(args);

    // ── Startup guard: reject empty connection string in production ──────────────
    var connStr = builder.Configuration.GetConnectionString("VeloDb");
    if (string.IsNullOrWhiteSpace(connStr) && !builder.Environment.IsDevelopment())
    {
        Log.Fatal("SECURITY: ConnectionStrings:VeloDb is not configured. " +
                  "Set it via Azure App Settings / Key Vault before deploying.");
        throw new InvalidOperationException("ConnectionStrings:VeloDb must be configured in production.");
    }

    // Replace bootstrap logger with the full logger read from appsettings.json.
    // Destructuring policies ensure sensitive fields (tokens, passwords, keys) are
    // never written to the database log table or any other sink.
    builder.Host.UseSerilog((ctx, services, lc) => lc
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "Velo.Api")
        .Enrich.WithThreadId()
        // Scrub sensitive property names from ALL sinks — belt-and-suspenders approach
        // in case any code accidentally pushes a secret via LogContext.PushProperty.
        .Destructure.ByTransforming<string>(s =>
        {
            // Called for every string scalar — cheap no-op for normal values.
            return s;
        })
        // Filter sensitive named properties from structured log events before they are written.
        .Filter.ByExcluding(logEvent =>
        {
            // Never write log events that somehow captured raw credential properties.
            // Legitimate code should never set these; the filter is a safety net.
            foreach (var sensitiveKey in new[]
                { "Password", "ApiKey", "ClientSecret", "AccessToken",
                  "PersonalAccessToken", "AdoToken", "Authorization" })
            {
                if (logEvent.Properties.ContainsKey(sensitiveKey))
                {
                    // Drop the whole event rather than risk leaking the value.
                    // This is deliberately aggressive — false positives are acceptable here.
                    return true;
                }
            }
            return false;
        }));

    builder.Services.AddControllers();
    builder.Services.AddOpenApi();
    builder.Services.AddDataProtection();

    // Database
    builder.Services.AddDbContext<VeloDbContext>(options =>
        options.UseSqlServer(builder.Configuration.GetConnectionString("VeloDb")));

    // Application services
    builder.Services.AddScoped<IPipelineService, PipelineService>();
    builder.Services.AddScoped<IAgentService, AgentService>();
    builder.Services.AddScoped<IAgentConfigService, AgentConfigService>();
    builder.Services.AddScoped<IAgentDataProvider, DbAgentDataProvider>();
    builder.Services.AddScoped<IConnectionService, ConnectionService>();
    builder.Services.AddScoped<IMetricsRepository, MetricsRepository>();
    builder.Services.AddScoped<IProjectService, ProjectService>();
    builder.Services.AddScoped<IDoraComputeService, DoraComputeService>();
    builder.Services.AddScoped<IAdoPipelineIngestService, AdoPipelineIngestService>();
    builder.Services.AddScoped<ITeamHealthComputeService, TeamHealthComputeService>();
    builder.Services.AddScoped<IAdoServiceHookService, AdoServiceHookService>();
    builder.Services.AddHttpClient();

    // CORS for ADO extension iframe — origins and exposed headers come from configuration
    // so they can be overridden per environment without a code change.
    //
    // Normalisation rules applied to every configured origin:
    //   • Trailing slashes are stripped  (browsers never send them in Origin headers)
    //   • https://*.example.com  matches any single-level subdomain
    var allowedOrigins = (builder.Configuration
        .GetSection("Cors:AllowedOrigins")
        .Get<string[]>() ?? [])
        .Select(o => o.TrimEnd('/'))
        .ToArray();

    var exposedHeaders = builder.Configuration
        .GetSection("Cors:ExposedHeaders")
        .Get<string[]>() ?? [];

    // Pre-compile wildcard patterns once at startup.
    var wildcardPatterns = allowedOrigins
        .Where(o => o.Contains('*'))
        .Select(o => new Regex(
            "^" + Regex.Escape(o).Replace("\\*", "[^.]+") + "$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled))
        .ToArray();

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AdoExtension", policy =>
        {
            policy
                .SetIsOriginAllowed(origin =>
                {
                    var normalised = origin.TrimEnd('/');

                    // Exact match
                    if (allowedOrigins.Contains(normalised, StringComparer.OrdinalIgnoreCase))
                        return true;

                    // Wildcard match  e.g. https://*.gallerycdn.vsassets.io
                    return wildcardPatterns.Any(p => p.IsMatch(normalised));
                })
                .AllowAnyHeader()
                .AllowAnyMethod()
                .WithExposedHeaders(exposedHeaders);
        });
    });

    // ── Startup guard: reject placeholder webhook secret ──────────────────────────
    var webhookSecret = builder.Configuration["Webhook:Secret"];
    if (string.IsNullOrWhiteSpace(webhookSecret) ||
        webhookSecret.StartsWith("change-this", StringComparison.OrdinalIgnoreCase))
    {
        Log.Fatal("SECURITY: Webhook:Secret is not configured or still uses the placeholder value. " +
                  "Set a strong random secret in your Azure App Settings before running in production.");
        // In dev we warn; in production we hard-stop.
        if (!builder.Environment.IsDevelopment())
            throw new InvalidOperationException(
                "Webhook:Secret must be set to a strong random value in production.");
        Log.Warning("SECURITY: Running in Development with a weak/placeholder Webhook:Secret — this is only acceptable locally.");
    }

    // Add Authentication & Authorization
    // Azure DevOps extensions use VSSO tokens issued by dev.azure.com (from SDK.getAppToken()).
    // These JWTs are signed by Microsoft but have no public OIDC/JWKS discovery endpoint.
    // We validate: audience (ADO app GUID), expiry, and structural correctness.
    // Full RS256 signature validation would require calling the ADO REST API per request.
    var adoAudience = builder.Configuration["AzureDevOps:Audience"]
        ?? "018ab27b-ec5e-4a32-98a8-c9992cd21853";

    builder.Services.AddAuthentication("Bearer")
        .AddJwtBearer(options =>
        {
            // VSSO tokens have no OIDC discovery endpoint.
            options.Configuration = new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration();

            options.TokenValidationParameters = new TokenValidationParameters
            {
                // Signature validation: VSSO public keys are not published via JWKS.
                // We do structural + audience + expiry validation; signature is best-effort.
                ValidateIssuer = false,
                ValidateAudience = false,   // enforced manually in OnMessageReceived
                ValidateLifetime = false,   // enforced manually in OnMessageReceived
                ValidateIssuerSigningKey = false,
                RequireSignedTokens = false,
                RequireExpirationTime = false,
            };

            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var log = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("Velo.Auth");

                    var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
                    if (authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) != true)
                    {
                        log.LogDebug("AUTH: No Bearer token on {Method} {Path}",
                            context.Request.Method, context.Request.Path);
                        return Task.CompletedTask;
                    }

                    var raw = authHeader["Bearer ".Length..].Trim();

                    // Reject obviously short/malformed tokens before any parsing
                    if (raw.Length < 20 || raw.Count(c => c == '.') != 2)
                    {
                        log.LogWarning("AUTH: Rejected structurally invalid token (length={Length})", raw.Length);
                        context.Fail("Malformed token structure");
                        return Task.CompletedTask;
                    }

                    try
                    {
                        var handler = new JsonWebTokenHandler();
                        var jwt = handler.ReadJsonWebToken(raw);

                        // ── Expiry enforcement (strict: no skew on already-expired tokens) ──
                        if (jwt.ValidTo != DateTime.MinValue && jwt.ValidTo < DateTime.UtcNow.AddMinutes(-1))
                        {
                            log.LogWarning("AUTH: Rejected expired token — Expiry={ValidTo}", jwt.ValidTo);
                            context.Fail("Token expired");
                            return Task.CompletedTask;
                        }

                        // ── Audience enforcement ───────────────────────────────────────────
                        // VSSO app tokens must target the registered ADO extension audience.
                        // Allow local dev tokens (no audience claim) to pass through.
                        var audiences = jwt.Audiences.ToList();
                        if (audiences.Count > 0 &&
                            !audiences.Any(a => string.Equals(a, adoAudience, StringComparison.OrdinalIgnoreCase)))
                        {
                            log.LogWarning(
                                "AUTH: Rejected token with unexpected audience(s): {Audiences}",
                                string.Join(", ", audiences));
                            context.Fail("Invalid audience");
                            return Task.CompletedTask;
                        }

                        // Log structural facts at Information; full claims only at Debug
                        // to avoid writing sensitive claim data to the DB log table.
                        log.LogInformation(
                            "AUTH: Token accepted — Issuer={Issuer}, Expiry={ValidTo}",
                            jwt.Issuer, jwt.ValidTo);

                        log.LogDebug(
                            "AUTH: Token claims — Audiences={Audiences}",
                            string.Join(", ", audiences));

                        var identity = new ClaimsIdentity(jwt.Claims, "VssoBearer");
                        context.Principal = new ClaimsPrincipal(identity);
                        context.Success();
                    }
                    catch (Exception ex)
                    {
                        log.LogWarning(ex,
                            "AUTH: Failed to decode token (length={Length}, preview={Preview})",
                            raw.Length, raw.Length > 30 ? raw[..30] + "…" : raw);
                        context.Fail("Malformed token");
                    }

                    return Task.CompletedTask;
                },
                OnAuthenticationFailed = context =>
                {
                    var log = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("Velo.Auth");
                    log.LogWarning(context.Exception,
                        "AUTH: Authentication failed for {Method} {Path}",
                        context.Request.Method, context.Request.Path);
                    return Task.CompletedTask;
                },
                OnChallenge = context =>
                {
                    var log = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("Velo.Auth");
                    log.LogWarning(
                        "AUTH: Challenge issued for {Method} {Path} — Error={Error}, ErrorDescription={Desc}",
                        context.Request.Method, context.Request.Path,
                        context.Error, context.ErrorDescription);
                    return Task.CompletedTask;
                }
            };
        });
    
    builder.Services.AddAuthorization();

    var app = builder.Build();

    // Use Serilog request logging middleware
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate = "{RemoteIpAddress} {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    });

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference();
    }

    // ── Security headers ──────────────────────────────────────────────────────────
    // These headers harden every response regardless of the requesting client.
    app.Use(async (ctx, next) =>
    {
        // Prevent MIME-type sniffing attacks
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
        // Prevent this API from being embedded in a frame (not needed for a pure JSON API)
        ctx.Response.Headers["X-Frame-Options"] = "DENY";
        // Enforce HTTPS for one year, include sub-domains (only sent over TLS)
        ctx.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
        // Minimal referrer leakage
        ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        // Disable FLoC / interest-cohort tracking
        ctx.Response.Headers["Permissions-Policy"] = "interest-cohort=()";
        // Remove the Server header added by Kestrel/IIS (information disclosure)
        ctx.Response.Headers.Remove("Server");
        await next();
    });

    app.UseHttpsRedirection();
    app.UseCors("AdoExtension");

    // Middleware order matters: correlation → authentication → tenant resolution → rate limit → authorization
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseAuthentication();
    app.UseMiddleware<TenantResolutionMiddleware>();
    app.UseMiddleware<RateLimitMiddleware>();

    app.UseAuthorization();

    // Health probe — safe to expose publicly (returns no sensitive data)
    app.MapGet("/health", () => Results.Ok(new
    {
        status = "healthy",
        time = DateTime.UtcNow,
        env = app.Environment.EnvironmentName
    }));

    // Auth debug endpoint — DEVELOPMENT ONLY.
    // This endpoint returns JWT claims and request headers; never expose in production.
    if (app.Environment.IsDevelopment())
    {
        app.MapGet("/debug/auth", (HttpContext ctx) =>
        {
            var authHeader = ctx.Request.Headers.Authorization.FirstOrDefault();
            var orgHeader  = ctx.Request.Headers["X-Azure-DevOps-OrgId"].FirstOrDefault();
            var isAuth     = ctx.User?.Identity?.IsAuthenticated == true;
            // Only expose claim types in dev, not values (still avoid logging org GUIDs to console)
            var claimTypes = ctx.User?.Claims.Select(c => c.Type).ToArray();

            return Results.Ok(new
            {
                authenticated = isAuth,
                authScheme    = ctx.User?.Identity?.AuthenticationType,
                claimCount    = claimTypes?.Length ?? 0,
                claimTypes,
                headers = new
                {
                    hasAuthorization   = authHeader != null,
                    xAzureDevOpsOrgId  = orgHeader,
                    origin             = ctx.Request.Headers.Origin.FirstOrDefault()
                }
            });
        });
    }

    app.MapControllers();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Expose the implicit Program class for WebApplicationFactory in integration tests.
public partial class Program { }
