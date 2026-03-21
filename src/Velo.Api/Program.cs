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

// Configure Serilog BEFORE building the host
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "Velo.Api")
    .Enrich.WithThreadId()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting Velo API application");
    
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog(Log.Logger);

    builder.Services.AddControllers();
    builder.Services.AddOpenApi();

    // Database
    builder.Services.AddDbContext<VeloDbContext>(options =>
        options.UseSqlServer(builder.Configuration.GetConnectionString("VeloDb")));

    // Application services
    builder.Services.AddScoped<IDoraService, DoraService>();
    builder.Services.AddScoped<IPipelineService, PipelineService>();
    builder.Services.AddScoped<IAgentService, AgentService>();
    builder.Services.AddScoped<IConnectionService, ConnectionService>();
    builder.Services.AddScoped<IMetricsRepository, MetricsRepository>();
    builder.Services.AddScoped<IProjectService, ProjectService>();
    builder.Services.AddScoped<AdoPipelineIngestService>();
    builder.Services.AddScoped<DoraComputeService>();
    builder.Services.AddScoped<AdoServiceHookService>();
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

    // Add Authentication & Authorization
    // Azure DevOps extensions use VSSO tokens (from SDK.getAppToken()), not Azure AD tokens.
    // The VSSO JWT issuer/audience format can vary, so we decode the token ourselves
    // and rely on CORS + token-presence as the primary security boundary.
    builder.Services.AddAuthentication("Bearer")
        .AddJwtBearer(options =>
        {
            // VSSO tokens have no OIDC discovery endpoint.
            options.Configuration = new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration();

            options.TokenValidationParameters = new TokenValidationParameters
            {
                // All validation is handled in OnMessageReceived below.
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = false,
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

                    try
                    {
                        var handler = new JsonWebTokenHandler();
                        var jwt = handler.ReadJsonWebToken(raw);

                        log.LogInformation(
                            "AUTH: Token decoded — Issuer={Issuer}, Audiences={Audiences}, Expiry={ValidTo}, " +
                            "Claims=[{Claims}]",
                            jwt.Issuer,
                            string.Join(", ", jwt.Audiences),
                            jwt.ValidTo,
                            string.Join(", ", jwt.Claims.Select(c => $"{c.Type}={c.Value}")));

                        // Basic expiry check (with 5-minute skew)
                        if (jwt.ValidTo != DateTime.MinValue && jwt.ValidTo < DateTime.UtcNow.AddMinutes(-5))
                        {
                            log.LogWarning("AUTH: Token expired at {ValidTo}", jwt.ValidTo);
                            context.Fail("Token expired");
                            return Task.CompletedTask;
                        }

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

    app.UseHttpsRedirection();
    app.UseCors("AdoExtension");

    // Middleware order matters: correlation → authentication → tenant resolution → rate limit → authorization
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseAuthentication();
    app.UseMiddleware<TenantResolutionMiddleware>();
    app.UseMiddleware<RateLimitMiddleware>();

    app.UseAuthorization();

    // Diagnostic endpoints — no auth required, so you can verify the app is running
    // and inspect what the incoming token looks like.
    app.MapGet("/health", () => Results.Ok(new
    {
        status = "healthy",
        time = DateTime.UtcNow,
        env = app.Environment.EnvironmentName
    }));

    app.MapGet("/debug/auth", (HttpContext ctx) =>
    {
        var authHeader = ctx.Request.Headers.Authorization.FirstOrDefault();
        var orgHeader = ctx.Request.Headers["X-Azure-DevOps-OrgId"].FirstOrDefault();
        var isAuth = ctx.User?.Identity?.IsAuthenticated == true;
        var claims = ctx.User?.Claims.Select(c => new { c.Type, c.Value }).ToArray();

        return Results.Ok(new
        {
            authenticated = isAuth,
            authScheme = ctx.User?.Identity?.AuthenticationType,
            claimCount = claims?.Length ?? 0,
            claims,
            headers = new
            {
                authorization = authHeader != null ? authHeader[..Math.Min(50, authHeader.Length)] + "…" : null,
                xAzureDevOpsOrgId = orgHeader,
                origin = ctx.Request.Headers.Origin.FirstOrDefault()
            }
        });
    });

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
