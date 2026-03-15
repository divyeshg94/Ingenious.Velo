using Velo.Api.Data;
using Velo.Api.Middleware;
using Velo.Api.Services;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

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

// CORS for ADO extension iframe
builder.Services.AddCors(options =>
{
    options.AddPolicy("AdoExtension", policy =>
    {
        policy.WithOrigins("https://dev.azure.com", "https://*.visualstudio.com")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseCors("AdoExtension");

app.UseMiddleware<TenantResolutionMiddleware>();
app.UseMiddleware<RateLimitMiddleware>();

app.UseAuthorization();
app.MapControllers();

app.Run();
