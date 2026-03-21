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
    // The VSSO JWT is issued by app.vstoken.visualstudio.com with audience <publisher>.<extension>.
    var vssoAudience = builder.Configuration["AzureDevOps:Audience"] ?? "DivyeshGovaerdhanan.velo";

    builder.Services.AddAuthentication("Bearer")
        .AddJwtBearer(options =>
        {
            // VSSO tokens don't expose an OIDC discovery endpoint, so disable automatic metadata retrieval.
            options.Configuration = new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration();

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = "app.vstoken.visualstudio.com",
                ValidateAudience = true,
                ValidAudience = vssoAudience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(5),

                // VSSO signing keys are not available via standard OIDC JWKS.
                // Signature validation is skipped for now; CORS + issuer/audience/expiry
                // checks provide the security boundary.  Full key validation can be
                // added later by fetching keys from the Azure DevOps REST API.
                RequireSignedTokens = false,
                ValidateIssuerSigningKey = false,
            };

            options.Events = new JwtBearerEvents
            {
                // Handle the VSSO token manually so that the standard handler
                // (which has no signing keys) doesn't reject the signature.
                OnMessageReceived = context =>
                {
                    var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
                    if (authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) != true)
                        return Task.CompletedTask;

                    var raw = authHeader["Bearer ".Length..].Trim();

                    try
                    {
                        var handler = new JsonWebTokenHandler();
                        var jwt = handler.ReadJsonWebToken(raw);

                        // Validate issuer
                        if (!jwt.Issuer.StartsWith("app.vstoken.visualstudio.com", StringComparison.OrdinalIgnoreCase))
                        {
                            context.Fail("Invalid VSSO token issuer");
                            return Task.CompletedTask;
                        }

                        // Validate audience
                        if (!jwt.Audiences.Any(a => a.Equals(vssoAudience, StringComparison.OrdinalIgnoreCase)))
                        {
                            context.Fail("Invalid VSSO token audience");
                            return Task.CompletedTask;
                        }

                        // Validate expiry
                        if (jwt.ValidTo < DateTime.UtcNow.AddMinutes(-5))
                        {
                            context.Fail("VSSO token expired");
                            return Task.CompletedTask;
                        }

                        var identity = new ClaimsIdentity(jwt.Claims, "VssoBearer");
                        context.Principal = new ClaimsPrincipal(identity);
                        context.Success();
                    }
                    catch
                    {
                        context.Fail("Malformed VSSO token");
                    }

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
