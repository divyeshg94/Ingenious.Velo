using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Velo.SQL;

namespace Velo.Api.IntegrationTests;

/// <summary>
/// Custom WebApplicationFactory that wires up an in-memory database and a
/// test-friendly middleware pipeline (no SQL Server session context calls).
/// Each factory instance gets its own isolated database.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Remove the SQL Server DbContext registration and replace with InMemory.
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<VeloDbContext>));
            if (descriptor != null)
                services.Remove(descriptor);

            services.AddDbContext<VeloDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));
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
