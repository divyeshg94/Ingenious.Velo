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
    public DbSet<PullRequestEvent> PullRequestEvents { get; set; } = null!;
    public DbSet<LogEvent> LogEvents { get; set; } = null!;
    public DbSet<TeamMapping> TeamMappings { get; set; } = null!;
    public DbSet<ProjectMapping> ProjectMappings { get; set; } = null!;
    public DbSet<AgentConfiguration> AgentConfigurations { get; set; } = null!;
    public DbSet<WorkItemEvent> WorkItemEvents { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Global tenant filter
        modelBuilder.Entity<PipelineRun>().HasQueryFilter(r => CurrentOrgId == null || r.OrgId == CurrentOrgId!);
        modelBuilder.Entity<DoraMetrics>().HasQueryFilter(r => CurrentOrgId == null || r.OrgId == CurrentOrgId!);
        modelBuilder.Entity<TeamHealth>().HasQueryFilter(r => CurrentOrgId == null || r.OrgId == CurrentOrgId!);
        modelBuilder.Entity<PullRequestEvent>().HasQueryFilter(r => CurrentOrgId == null || r.OrgId == CurrentOrgId!);
        modelBuilder.Entity<TeamMapping>().HasQueryFilter(r => CurrentOrgId == null || r.OrgId == CurrentOrgId!);
        modelBuilder.Entity<WorkItemEvent>().HasQueryFilter(r => CurrentOrgId == null || r.OrgId == CurrentOrgId!);

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
        ConfigureAuditable<PullRequestEvent>();
        ConfigureAuditable<TeamMapping>();
        ConfigureAuditable<WorkItemEvent>();

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

        modelBuilder.Entity<PipelineRun>()
            .HasIndex(r => new { r.OrgId, r.ProjectId, r.RepositoryName })
            .HasDatabaseName("IX_PipelineRuns_OrgId_ProjectId_RepositoryName");

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

        // PullRequestEvents indexes
        modelBuilder.Entity<PullRequestEvent>()
            .HasIndex(p => new { p.OrgId, p.ProjectId, p.CreatedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("IX_PullRequestEvents_OrgId_ProjectId_CreatedAt_DESC");

        modelBuilder.Entity<PullRequestEvent>()
            .HasIndex(p => new { p.OrgId, p.PrId, p.Status })
            .HasDatabaseName("IX_PullRequestEvents_OrgId_PrId_Status");

        // WorkItemEvents indexes
        modelBuilder.Entity<WorkItemEvent>()
            .HasIndex(w => new { w.OrgId, w.ProjectId, w.ChangedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("IX_WorkItemEvents_OrgId_ProjectId_ChangedAt_DESC");

        modelBuilder.Entity<WorkItemEvent>()
            .HasIndex(w => new { w.OrgId, w.WorkItemId })
            .HasDatabaseName("IX_WorkItemEvents_OrgId_WorkItemId");

        // TeamMappings indexes
        modelBuilder.Entity<TeamMapping>()
            .HasIndex(m => new { m.OrgId, m.ProjectId })
            .HasDatabaseName("IX_TeamMappings_OrgId_ProjectId");

        modelBuilder.Entity<TeamMapping>()
            .HasIndex(m => new { m.OrgId, m.ProjectId, m.RepositoryName })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("IX_TeamMappings_OrgId_ProjectId_RepositoryName_Unique");

        // LogEvents — no tenant filter (logs are cross-org for ops/audit)
        // Table name matches Serilog MSSqlServer sink tableName in appsettings.json
        modelBuilder.Entity<LogEvent>(eb =>
        {
            eb.ToTable("log_events");
            eb.HasKey(e => e.Id);
            eb.Property(e => e.Message).HasMaxLength(4000);
            eb.Property(e => e.MessageTemplate).HasMaxLength(4000).IsRequired();
            eb.Property(e => e.Level).HasMaxLength(128).IsRequired();
            eb.Property(e => e.TimeStamp).HasDefaultValueSql("SYSUTCDATETIME()");
            eb.Property(e => e.OrgId).HasMaxLength(256);
            eb.Property(e => e.UserId).HasMaxLength(256);
            eb.Property(e => e.CorrelationId).HasMaxLength(128);
            eb.Property(e => e.RequestPath).HasMaxLength(1024);
            eb.Property(e => e.RequestMethod).HasMaxLength(10);

            eb.HasIndex(e => e.TimeStamp)
              .IsDescending(true)
              .HasDatabaseName("IX_log_events_TimeStamp_DESC");

            eb.HasIndex(e => new { e.Level, e.TimeStamp })
              .IsDescending(false, true)
              .HasDatabaseName("IX_log_events_Level_TimeStamp");

            eb.HasIndex(e => new { e.OrgId, e.TimeStamp })
              .IsDescending(false, true)
              .HasDatabaseName("IX_log_events_OrgId_TimeStamp");

            eb.HasIndex(e => e.CorrelationId)
              .HasDatabaseName("IX_log_events_CorrelationId");
        });

        // ProjectMappings — no tenant query filter; table is org-scoped but not row-level-secured
        modelBuilder.Entity<ProjectMapping>(eb =>
        {
            eb.Property(p => p.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            eb.HasIndex(p => new { p.OrgId, p.ProjectGuid })
              .IsUnique()
              .HasDatabaseName("IX_ProjectMappings_OrgId_ProjectGuid");
        });

        // AgentConfigurations — one row per org, no tenant query filter
        modelBuilder.Entity<AgentConfiguration>(eb =>
        {
            eb.Property(a => a.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            eb.Property(a => a.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            eb.HasIndex(a => a.OrgId)
              .IsUnique()
              .HasDatabaseName("IX_AgentConfigurations_OrgId");
        });

        base.OnModelCreating(modelBuilder);
    }
}
