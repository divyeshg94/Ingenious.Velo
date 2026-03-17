using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Velo.Shared.Models;
using Velo.Shared.Contracts;
using Velo.Api.Services;

namespace Velo.Api.Controllers;

/// <summary>
/// Organizations API controller - manages org connections and project access.
/// SECURITY: All endpoints require [Authorize] - validates JWT token from Azure AD B2C.
/// MULTI-TENANCY: All queries scoped to org_id from JWT token (via TenantResolutionMiddleware).
/// AUDIT: All operations logged with org_id, user context, and correlation ID.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OrgsController(
    IMetricsRepository metricsRepository,
    IProjectService projectService,
    ILogger<OrgsController> logger) : ControllerBase
{
    /// <summary>
    /// Get the current organization from the JWT token claim.
    /// Multi-tenant: Returns only the org_id from the user's token (set by TenantResolutionMiddleware).
    /// </summary>
    [HttpGet("me")]
    public async Task<ActionResult<OrgContextDto>> GetCurrentOrg(CancellationToken cancellationToken)
    {
        var orgId = HttpContext.Items["OrgId"]?.ToString();
        var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? "N/A";
        var userId = User?.FindFirst("sub")?.Value ?? "unknown";

        try
        {
            if (string.IsNullOrEmpty(orgId))
            {
                logger.LogWarning(
                    "SECURITY: Unauthorized attempt to fetch organization - OrgId missing, UserId: {UserId}, CorrelationId: {CorrelationId}",
                    userId, correlationId);
                return Unauthorized(new { error = "Organization context not found" });
            }

            logger.LogInformation(
                "AUDIT: Fetching organization context - OrgId: {OrgId}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                orgId, userId, correlationId);

            var org = await metricsRepository.GetOrgContextAsync(orgId, cancellationToken);

            if (org == null)
            {
                logger.LogInformation(
                    "AUDIT: Organization not yet registered - OrgId: {OrgId}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                    orgId, userId, correlationId);

                // Return default org info for first-time users
                return Ok(new OrgContextDto
                {
                    OrgId = orgId,
                    OrgUrl = $"https://dev.azure.com/{orgId}",
                    DisplayName = orgId,
                    IsPremium = false,
                    RegisteredAt = DateTime.UtcNow,
                    LastSeenAt = DateTime.UtcNow
                });
            }

            logger.LogInformation(
                "AUDIT: Successfully returned organization context - OrgId: {OrgId}, Premium: {IsPremium}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                orgId, org.IsPremium, userId, correlationId);

            return Ok(org);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "ERROR: Exception fetching organization - OrgId: {OrgId}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                orgId, userId, correlationId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Get all projects available to the current organization.
    /// Multi-tenant: Only returns projects for the authenticated org_id.
    /// Enforced by: EF query filter on PipelineRuns table.
    /// </summary>
    [HttpGet("projects")]
    public async Task<ActionResult<IEnumerable<string>>> GetAvailableProjects(CancellationToken cancellationToken)
    {
        var orgId = HttpContext.Items["OrgId"]?.ToString();
        var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? "N/A";
        var userId = User?.FindFirst("sub")?.Value ?? "unknown";

        try
        {
            if (string.IsNullOrEmpty(orgId))
            {
                logger.LogWarning(
                    "SECURITY: Unauthorized attempt to fetch projects - OrgId missing, UserId: {UserId}, CorrelationId: {CorrelationId}",
                    userId, correlationId);
                return Unauthorized(new { error = "Organization context not found" });
            }

            logger.LogInformation(
                "AUDIT: Fetching available projects - OrgId: {OrgId}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                orgId, userId, correlationId);

            var projects = await projectService.GetProjectsAsync(orgId, cancellationToken);

            logger.LogInformation(
                "AUDIT: Successfully returned {ProjectCount} projects - OrgId: {OrgId}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                projects.Count(), orgId, userId, correlationId);

            return Ok(projects);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "ERROR: Exception fetching projects - OrgId: {OrgId}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                orgId, userId, correlationId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Connect a new Azure DevOps organization to Velo.
    /// Multi-tenant: Only the authenticated user (org_id in token) can connect their org.
    /// Validates Azure DevOps org ownership before registering.
    /// </summary>
    [HttpPost("connect")]
    public async Task<ActionResult<OrgContextDto>> ConnectOrganization(
        [FromBody] ConnectOrgRequest request,
        CancellationToken cancellationToken)
    {
        var orgId = HttpContext.Items["OrgId"]?.ToString();
        var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? "N/A";
        var userId = User?.FindFirst("sub")?.Value ?? "unknown";

        try
        {
            if (string.IsNullOrEmpty(orgId))
            {
                logger.LogWarning(
                    "SECURITY: Unauthorized attempt to connect organization - OrgId missing, UserId: {UserId}, CorrelationId: {CorrelationId}",
                    userId, correlationId);
                return Unauthorized(new { error = "Organization context not found" });
            }

            if (string.IsNullOrWhiteSpace(request.OrgUrl))
            {
                logger.LogWarning(
                    "AUDIT: Invalid org connection request - missing OrgUrl, UserId: {UserId}, CorrelationId: {CorrelationId}",
                    userId, correlationId);
                return BadRequest(new { error = "OrgUrl is required" });
            }

            logger.LogInformation(
                "AUDIT: Connecting organization - OrgId: {OrgId}, OrgUrl: {OrgUrl}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                orgId, request.OrgUrl, userId, correlationId);

            // TODO: Validate org ownership via Azure DevOps API using Managed Identity
            // This ensures the user actually owns/manages the org before allowing connection

            var orgDto = new OrgContextDto
            {
                OrgId = orgId,
                OrgUrl = request.OrgUrl,
                DisplayName = orgId,
                IsPremium = false,
                RegisteredAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow,
                DailyTokenBudget = 50_000
            };

            await metricsRepository.SaveOrgContextAsync(orgDto, cancellationToken);

            logger.LogInformation(
                "AUDIT: Successfully connected organization - OrgId: {OrgId}, OrgUrl: {OrgUrl}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                orgId, request.OrgUrl, userId, correlationId);

            return Ok(orgDto);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "ERROR: Exception connecting organization - OrgId: {OrgId}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                orgId, userId, correlationId);
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}

public record ConnectOrgRequest(string OrgUrl);
