using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Velo.SQL;

// Shared internal service provider ensures InMemory EF Core services are isolated
// from the SQL Server services registered in the main application DI container,
// preventing the "multiple database providers" exception at test runtime.

namespace Velo.Api.IntegrationTests;

/// <summary>
/// Custom WebApplicationFactory that wires up an in-memory database and a
/// test-friendly middleware pipeline (no SQL Server session context calls).
/// Each factory instance gets its own isolated database.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    // Built once and shared: each factory uses the same internal EF Core infrastructure
    // but a unique _dbName, keeping each test class's data completely isolated.
    private static readonly IServiceProvider _inMemoryServiceProvider =
        new ServiceCollection()
            .AddEntityFrameworkInMemoryDatabase()
            .BuildServiceProvider();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Satisfy Program.cs startup guards that reject missing config when
        // the environment is not "Development". The actual DbContext is
        // replaced below with an in-memory database, so the connection string
        // value is never used.
        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:VeloDb"] = "Server=test;Database=VeloTest;",
                ["Webhook:Secret"] = "test-webhook-secret-for-integration-tests",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove the SQL Server DbContext registration and replace with InMemory.
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<VeloDbContext>));
            if (descriptor != null)
                services.Remove(descriptor);

            services.AddDbContext<VeloDbContext>(options =>
                options.UseInMemoryDatabase(_dbName)
                       .UseInternalServiceProvider(_inMemoryServiceProvider));
        });
    }

    /// <summary>
    /// Returns an HttpClient with a pre-configured JWT bearer token and
    /// X-Azure-DevOps-OrgId header so every request is authenticated and
    /// tenant-resolved without touching a real Azure AD endpoint.
    /// </summary>
    public HttpClient CreateAuthenticatedClient(string orgId = "test-org")
    {
        var client = CreateClient();
        var token = JwtHelper.CreateToken(orgId);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Azure-DevOps-OrgId", orgId);
        return client;
    }
}
