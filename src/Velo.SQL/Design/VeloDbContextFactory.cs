using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Velo.SQL.Design;

public class VeloDbContextFactory : IDesignTimeDbContextFactory<VeloDbContext>
{
    public VeloDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<VeloDbContext>();

        // Try to read connection string from the API project's appsettings.json
        string? connectionString = null;
        try
        {
            var baseDir = Directory.GetCurrentDirectory();
            var candidate = Path.Combine(baseDir, "..", "Velo.Api", "appsettings.json");
            if (!File.Exists(candidate))
            {
                // fallback: try src/Velo.Api relative to solution root
                candidate = Path.Combine(baseDir, "..", "..", "..", "src", "Velo.Api", "appsettings.json");
            }

            if (File.Exists(candidate))
            {
                var config = new ConfigurationBuilder()
                    .AddJsonFile(candidate, optional: false, reloadOnChange: false)
                    .Build();

                connectionString = config.GetConnectionString("VeloDb");
            }
        }
        catch
        {
            // ignore and fallback to localdb
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = "Server=(localdb)\\mssqllocaldb;Database=VeloDb;Trusted_Connection=True;MultipleActiveResultSets=true";
        }

        builder.UseSqlServer(connectionString);
        return new VeloDbContext(builder.Options);
    }
}
