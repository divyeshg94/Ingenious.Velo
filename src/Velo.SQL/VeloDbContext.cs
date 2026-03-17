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

        // Configure OrgContext
        modelBuilder.Entity<OrgContext>(eb =>
        {
            eb.HasKey(o => o.OrgId);
            eb.Property(p => p.CreatedBy).HasMaxLength(200);
            eb.Property(p => p.CreatedDate).HasDefaultValueSql("SYSUTCDATETIME()");
            eb.Property(p => p.ModifiedBy).HasMaxLength(200);
            eb.Property(p => p.IsDeleted).HasDefaultValue(false);
        });

        void ConfigureAuditable<T>() where T : AuditableEntity
        {
            modelBuilder.Entity<T>(eb =>
            {
                eb.Property(p => p.CreatedBy).HasMaxLength(200);
                eb.Property(p => p.CreatedDate).HasDefaultValueSql("SYSUTCDATETIME()");
                eb.Property(p => p.ModifiedBy).HasMaxLength(200);
                eb.Property(p => p.IsDeleted).HasDefaultValue(false);
            });
        }

        ConfigureAuditable<PipelineRun>();
        ConfigureAuditable<DoraMetrics>();
        ConfigureAuditable<TeamHealth>();

        // Configure indexes for performance and multi-tenancy

        // PipelineRuns indexes
        modelBuilder.Entity<PipelineRun>()
            .HasIndex(r => new { r.OrgId, r.ProjectId, r.StartTime })
            .IsDescending(false, false, true)
            .HasDatabaseName("IX_PipelineRuns_OrgId_ProjectId_StartTime_DESC");

        modelBuilder.Entity<PipelineRun>()
            .HasIndex(r => new { r.OrgId, r.IsDeployment })
            .HasDatabaseName("IX_PipelineRuns_OrgId_IsDeployment");

        modelBuilder.Entity<PipelineRun>()
            .HasIndex(r => r.StartTime)
            .IsDescending(true)
            .HasDatabaseName("IX_PipelineRuns_StartTime_DESC");

        // DoraMetrics indexes
        modelBuilder.Entity<DoraMetrics>()
            .HasIndex(m => new { m.OrgId, m.ProjectId, m.ComputedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("IX_DoraMetrics_OrgId_ProjectId_ComputedAt_DESC");

        modelBuilder.Entity<DoraMetrics>()
            .HasIndex(m => m.ComputedAt)
            .IsDescending(true)
            .HasDatabaseName("IX_DoraMetrics_ComputedAt_DESC");

        // TeamHealth indexes
        modelBuilder.Entity<TeamHealth>()
            .HasIndex(h => new { h.OrgId, h.ProjectId, h.ComputedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("IX_TeamHealth_OrgId_ProjectId_ComputedAt_DESC");

        modelBuilder.Entity<TeamHealth>()
            .HasIndex(h => h.ComputedAt)
            .IsDescending(true)
            .HasDatabaseName("IX_TeamHealth_ComputedAt_DESC");

        base.OnModelCreating(modelBuilder);
    }
}
