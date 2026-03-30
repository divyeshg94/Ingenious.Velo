using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Velo.Shared.Contracts;
using Velo.Shared.Models;

namespace Velo.Api.Controllers;

/// <summary>
/// Manages team → repository mappings for multi-team/multi-repo scenarios.
/// Teams label their ADO repositories with a friendly team name so that DORA
/// and Health metrics can be filtered per team.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TeamMappingsController(
    IMetricsRepository repo,
    ILogger<TeamMappingsController> logger) : ControllerBase
{
    /// <summary>List all team→repo mappings for a project.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TeamMappingDto>>> GetMappings(
        [FromQuery] string projectId,
        CancellationToken cancellationToken = default)
    {
        var orgId = HttpContext.Items["OrgId"]?.ToString();
        if (string.IsNullOrEmpty(orgId)) return Unauthorized();
        if (string.IsNullOrWhiteSpace(projectId)) return BadRequest(new { error = "projectId is required" });

        var mappings = await repo.GetTeamMappingsAsync(orgId, projectId, cancellationToken);
        return Ok(mappings);
    }

    /// <summary>List all distinct repository names seen in pipeline runs for a project.</summary>
    [HttpGet("repositories")]
    public async Task<ActionResult<IEnumerable<string>>> GetRepositories(
        [FromQuery] string projectId,
        CancellationToken cancellationToken = default)
    {
        var orgId = HttpContext.Items["OrgId"]?.ToString();
        if (string.IsNullOrEmpty(orgId)) return Unauthorized();
        if (string.IsNullOrWhiteSpace(projectId)) return BadRequest(new { error = "projectId is required" });

        var repos = await repo.GetDistinctRepositoriesAsync(orgId, projectId, cancellationToken);
        return Ok(repos);
    }

    /// <summary>Create or update a team→repo mapping (upsert on RepositoryName).</summary>
    [HttpPost]
    public async Task<ActionResult<TeamMappingDto>> SaveMapping(
        [FromBody] TeamMappingDto dto,
        CancellationToken cancellationToken = default)
    {
        var orgId = HttpContext.Items["OrgId"]?.ToString();
        if (string.IsNullOrEmpty(orgId)) return Unauthorized();

        if (string.IsNullOrWhiteSpace(dto.ProjectId)) return BadRequest(new { error = "projectId is required" });
        if (string.IsNullOrWhiteSpace(dto.RepositoryName)) return BadRequest(new { error = "repositoryName is required" });
        if (string.IsNullOrWhiteSpace(dto.TeamName)) return BadRequest(new { error = "teamName is required" });

        dto.OrgId = orgId;

        try
        {
            await repo.SaveTeamMappingAsync(dto, cancellationToken);
            logger.LogInformation(
                "TEAM_MAPPING: Saved repo={Repo} → team={Team} for OrgId={OrgId}, ProjectId={Project}",
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(dto.RepositoryName), Velo.Api.Logging.LogSanitizer.SanitiseForLog(dto.TeamName), Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(dto.ProjectId));
            return Ok(dto);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "TEAM_MAPPING: Failed to save mapping");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>Delete a team→repo mapping by ID (soft-delete).</summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> DeleteMapping(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var orgId = HttpContext.Items["OrgId"]?.ToString();
        if (string.IsNullOrEmpty(orgId)) return Unauthorized();

        try
        {
            await repo.DeleteTeamMappingAsync(id, orgId, cancellationToken);
            logger.LogInformation("TEAM_MAPPING: Deleted mapping {Id} for OrgId={OrgId}", id, Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId));
            return NoContent();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "TEAM_MAPPING: Failed to delete mapping {Id}", id);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}
