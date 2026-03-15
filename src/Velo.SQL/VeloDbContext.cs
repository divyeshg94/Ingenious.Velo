using Microsoft.EntityFrameworkCore;
using Velo.SQL.Models;

namespace Velo.SQL;

public class VeloDbContext : DbContext
{
    public VeloDbContext(DbContextOptions<VeloDbContext> options) : base(options) { }

    public string? CurrentOrgId { get; set; }

    public DbSet<PipelineRun> PipelineRuns { get; set; } = null!;
    public DbSet<DoraMetrics> DoraMetrics { get; set; } = null!;
    public DbSet<OrgContext> Organizations { get; set; } = null!;
    public DbSet<TeamHealth> TeamHealth { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Global tenant filter
        modelBuilder.Entity<PipelineRun>().HasQueryFilter(r => CurrentOrgId == null || r.OrgId == CurrentOrgId!);
        modelBuilder.Entity<DoraMetrics>().HasQueryFilter(r => CurrentOrgId == null || r.OrgId == CurrentOrgId!);
        modelBuilder.Entity<TeamHealth>().HasQueryFilter(r => CurrentOrgId == null || r.OrgId == CurrentOrgId!);

        modelBuilder.Entity<OrgContext>(eb =>
        {
            eb.HasKey(o => o.OrgId);
        });

        base.OnModelCreating(modelBuilder);
    }
}
