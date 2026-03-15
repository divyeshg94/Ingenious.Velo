using Microsoft.EntityFrameworkCore;
using Velo.Shared.Models;

namespace Velo.Api.Data;

public class VeloDbContext(DbContextOptions<VeloDbContext> options) : DbContext(options)
{
    public DbSet<OrgContext> Organizations => Set<OrgContext>();
    public DbSet<PipelineRun> PipelineRuns => Set<PipelineRun>();
    public DbSet<DoraMetrics> DoraMetrics => Set<DoraMetrics>();
    public DbSet<TeamHealth> TeamHealth => Set<TeamHealth>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Row-level security filter: each query scoped to the current org
        // Actual RLS policy is enforced at the SQL Server level (see 006_row_level_security.sql)
        modelBuilder.Entity<PipelineRun>().HasQueryFilter(r => r.OrgId == CurrentOrgId);
        modelBuilder.Entity<DoraMetrics>().HasQueryFilter(m => m.OrgId == CurrentOrgId);
        modelBuilder.Entity<TeamHealth>().HasQueryFilter(h => h.OrgId == CurrentOrgId);
    }

    // Set by TenantResolutionMiddleware via scoped service
    public string CurrentOrgId { get; set; } = string.Empty;
}
