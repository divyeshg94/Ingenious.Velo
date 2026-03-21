using Serilog;
using Serilog.Events;
using System.Text.RegularExpressions;
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
    builder.Services.AddAuthentication("Bearer")
        .AddJwtBearer(options =>
        {
            options.Authority = builder.Configuration["Azure:Authority"] ?? "https://login.microsoftonline.com/common";
            options.Audience = builder.Configuration["Azure:Audience"];
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
