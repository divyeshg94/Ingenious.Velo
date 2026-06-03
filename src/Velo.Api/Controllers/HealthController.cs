using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Velo.Api.Services;
using Velo.Shared.Contracts;
using Velo.Shared.Models;

namespace Velo.Api.Controllers;

/// <summary>
/// Team Health API controller — returns pipeline-derived health signals per project.
///
/// SECURITY: All endpoints require [Authorize] — JWT validated by AuthMiddleware.
/// MULTI-TENANCY: org_id from TenantResolutionMiddleware scopes all queries automatically.
/// AUTO-COMPUTE: If no health snapshot exists the controller computes one inline,
///               matching the same auto-recovery pattern used by DoraController.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class HealthController(
    IMetricsRepository metricsRepository,
    ITeamHealthComputeService healthService,
    ILogger<HealthController> logger) : ControllerBase
{
    /// <summary>
    /// Returns the latest Team Health snapshot for a project.
    /// Auto-computes and saves a new snapshot if none exists.
    /// </summary>
    [HttpGet("team")]
    public async Task<ActionResult<TeamHealthDto>> GetTeamHealth(
        [FromQuery] string projectId,
        [FromQuery] string? repositoryName = null,
        [FromQuery] string? teamName = null,
        CancellationToken cancellationToken = default)
    {
        var orgId         = HttpContext.Items["OrgId"]?.ToString();
        var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? "N/A";
        var userId        = User?.FindFirst("sub")?.Value ?? "unknown";

        if (string.IsNullOrEmpty(orgId))
        {
            logger.LogWarning(
                "SECURITY: Unauthorized team-health request — OrgId missing, " +
                "UserId={UserId}, CorrelationId={CorrelationId}",
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(userId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(correlationId));
            return Unauthorized(new { error = "Organization context not found" });
        }

        if (string.IsNullOrEmpty(projectId))
            return BadRequest(new { error = "projectId is required" });

        logger.LogInformation(
            "AUDIT: GetTeamHealth — OrgId={OrgId}, ProjectId={ProjectId}, " +
            "RepositoryName={RepositoryName}, TeamName={TeamName}, UserId={UserId}, CorrelationId={CorrelationId}",
            Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(repositoryName ?? "(all)"), Velo.Api.Logging.LogSanitizer.SanitiseForLog(teamName ?? "(all)"), Velo.Api.Logging.LogSanitizer.SanitiseForLog(userId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(correlationId));

        try
        {
            // Resolve the effective storage key so cached snapshots are actually hit
            // for team filters (snapshots are keyed by the single mapped repo name
            // when N=1, or "team:<teamName-lower>" when N>1).
            string? filterKey;
            try
            {
                filterKey = await ResolveFilterKeyAsync(orgId, projectId, repositoryName, teamName, cancellationToken);
            }
            catch (TeamHasNoMappingsException)
            {
                return Ok(new TeamHealthResponse
                {
                    Status = "empty",
                    Note = "Team has no mapped pipelines",
                    OrgId = orgId,
                    ProjectId = projectId,
                    TeamName = teamName
                });
            }

            var health = await metricsRepository.GetTeamHealthAsync(orgId, projectId, filterKey, cancellationToken);

            if (health == null)
            {
                logger.LogInformation(
                    "HEALTH: No snapshot found — computing inline. " +
                    "OrgId={OrgId}, ProjectId={ProjectId}", Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId));
                try
                {
                    health = await healthService.ComputeAndSaveAsync(orgId, projectId, repositoryName, teamName, cancellationToken);
                }
                catch (TeamHasNoMappingsException)
                {
                    return Ok(new TeamHealthResponse
                    {
                        Status = "empty",
                        Note = "Team has no mapped pipelines",
                        OrgId = orgId,
                        ProjectId = projectId,
                        TeamName = teamName
                    });
                }
            }

            return Ok(health);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "ERROR: GetTeamHealth failed — OrgId={OrgId}, ProjectId={ProjectId}, " +
                "CorrelationId={CorrelationId}", Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(correlationId));
            return StatusCode(500, new { error = "Failed to load team health metrics" });
        }
    }

    /// <summary>
    /// Force-recomputes and saves a fresh Team Health snapshot.
    /// Called when the user clicks "Refresh" in the UI.
    /// </summary>
    [HttpPost("recompute")]
    public async Task<ActionResult<TeamHealthDto>> Recompute(
        [FromQuery] string projectId,
        [FromQuery] string? repositoryName = null,
        [FromQuery] string? teamName = null,
        CancellationToken cancellationToken = default)
    {
        var orgId         = HttpContext.Items["OrgId"]?.ToString();
        var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? "N/A";
        var userId        = User?.FindFirst("sub")?.Value ?? "unknown";

        if (string.IsNullOrEmpty(orgId))
            return Unauthorized(new { error = "Organization context not found" });

        if (string.IsNullOrEmpty(projectId))
            return BadRequest(new { error = "projectId is required" });

        logger.LogInformation(
            "AUDIT: ForceComputeHealth — OrgId={OrgId}, ProjectId={ProjectId}, " +
            "RepositoryName={RepositoryName}, TeamName={TeamName}, UserId={UserId}, CorrelationId={CorrelationId}",
            Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(repositoryName ?? "(all)"), Velo.Api.Logging.LogSanitizer.SanitiseForLog(teamName ?? "(all)"), Velo.Api.Logging.LogSanitizer.SanitiseForLog(userId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(correlationId));

        try
        {
            var health = await healthService.ComputeAndSaveAsync(orgId, projectId, repositoryName, teamName, cancellationToken);
            return Ok(health);
        }
        catch (TeamHasNoMappingsException)
        {
            return Ok(new TeamHealthResponse
            {
                Status = "empty",
                Note = "Team has no mapped pipelines",
                OrgId = orgId,
                ProjectId = projectId,
                TeamName = teamName
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "ERROR: ForceComputeHealth failed — OrgId={OrgId}, ProjectId={ProjectId}",
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId));
            return StatusCode(500, new { error = "Failed to compute team health metrics" });
        }
    }

    /// <summary>
    /// Mirrors <see cref="TeamHealthComputeService"/>'s ResolveFilterAsync logic so the
    /// controller can hit the correct cached snapshot. Returns:
    ///   • the trimmed repositoryName when supplied,
    ///   • the single mapped repo when teamName maps to N=1,
    ///   • "team:&lt;teamName-lower&gt;" when teamName maps to N&gt;1,
    ///   • null when neither filter is supplied (project-wide aggregate).
    /// Throws <see cref="TeamHasNoMappingsException"/> when teamName resolves to N=0.
    /// </summary>
    private async Task<string?> ResolveFilterKeyAsync(
        string orgId, string projectId, string? repositoryName, string? teamName,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(repositoryName))
            return repositoryName.Trim();

        if (!string.IsNullOrWhiteSpace(teamName))
        {
            var team = teamName.Trim();
            var mappings = (await metricsRepository.GetTeamMappingsAsync(orgId, projectId, cancellationToken)).ToList();
            var repos = mappings
                .Where(m => string.Equals(m.TeamName, team, StringComparison.OrdinalIgnoreCase))
                .Select(m => m.RepositoryName)
                .Where(r => !string.IsNullOrEmpty(r))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (repos.Count == 0)
                throw new TeamHasNoMappingsException(team);

            return repos.Count == 1 ? repos[0] : $"team:{team.ToLowerInvariant()}";
        }

        return null;
    }
}
