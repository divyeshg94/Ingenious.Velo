using Microsoft.EntityFrameworkCore;
using Velo.SQL;

namespace Velo.Api.Interface;

/// <summary>
/// Project service contract - handles project-related operations scoped to org_id.
/// </summary>
public interface IProjectService
{
    Task<IEnumerable<string>> GetProjectsAsync(string orgId, CancellationToken cancellationToken);
    Task<bool> ValidateProjectAccessAsync(string orgId, string projectId, CancellationToken cancellationToken);
}

/// <summary>
/// Project service implementation - retrieves projects for an organization.
/// SECURITY: All operations scoped to org_id via EF Core query filters.
/// </summary>
public class ProjectService(VeloDbContext dbContext, ILogger<ProjectService> logger) : IProjectService
{
    /// <summary>
    /// Get all unique projects for an organization based on pipeline runs.
    /// </summary>
    public async Task<IEnumerable<string>> GetProjectsAsync(string orgId, CancellationToken cancellationToken)
    {
        try
        {
            var projects = await dbContext.PipelineRuns
                .AsNoTracking()
                .Where(r => r.OrgId == orgId)
                .Select(r => r.ProjectId)
                .Distinct()
                .OrderBy(p => p)
                .ToListAsync(cancellationToken);

            logger.LogInformation(
                "Retrieved {ProjectCount} projects for OrgId: {OrgId}",
                projects.Count, Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId));

            return projects;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching projects for OrgId: {OrgId}", Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId));
            throw;
        }
    }

    /// <summary>
    /// Validate that a project belongs to an organization.
    /// </summary>
    public async Task<bool> ValidateProjectAccessAsync(string orgId, string projectId, CancellationToken cancellationToken)
    {
        try
        {
            var exists = await dbContext.PipelineRuns
                .AsNoTracking()
                .AnyAsync(r => r.OrgId == orgId && r.ProjectId == projectId, cancellationToken);

            if (!exists)
            {
                logger.LogWarning(
                    "SECURITY: Unauthorized project access attempt - OrgId: {OrgId}, ProjectId: {ProjectId}",
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId),
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId));
            }

            return exists;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error validating project access for OrgId: {OrgId}, ProjectId: {ProjectId}",
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId),
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId));
            throw;
        }
    }
}
