using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Velo.Shared.Models;
using Velo.Shared.Contracts;
using Velo.Api.Services;

namespace Velo.Api.Controllers;

/// <summary>
/// DORA Metrics API controller with comprehensive security and audit logging.
/// SECURITY: All endpoints require [Authorize] - validates JWT token from Azure AD B2C.
/// AUDIT: All operations logged with org_id, user context, and correlation ID.
/// MULTI-TENANCY: All queries automatically filtered by org_id (from JWT token → TenantResolutionMiddleware → VeloDbContext.CurrentOrgId).
/// ROW-LEVEL SECURITY: SQL Server RLS policies enforce org_id scoping at the database layer.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DoraController(
    IMetricsRepository metricsRepository,
    IServiceScopeFactory scopeFactory,
    ILogger<DoraController> logger) : ControllerBase
{
    private const string AdoTokenHeader = "X-Ado-Access-Token";

    /// <summary>
    /// Get the latest DORA metrics for a project.
    /// Multi-tenant: Only returns metrics for the authenticated org_id.
    /// Enforced by: EF Core global query filter + SQL Server RLS.
    ///
    /// AUTO-RECOVERY: When no metrics exist and X-Ado-Access-Token is present,
    /// automatically triggers a background sync for this project so metrics appear
    /// without requiring the user to visit the Connections tab first. This recovers
    /// from webhook failures or missed events without any manual intervention.
    /// </summary>
    [HttpGet("latest")]
    public async Task<ActionResult<DoraMetricsResponse>> GetLatestMetrics(
        [FromQuery] string projectId,
        [FromQuery] string? repositoryName = null,
        [FromQuery] string? teamName = null,
        CancellationToken cancellationToken = default)
    {
        var orgId = HttpContext.Items["OrgId"]?.ToString();
        var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? "N/A";
        var userId = User?.FindFirst("sub")?.Value ?? "unknown";
        var adoToken = Request.Headers[AdoTokenHeader].FirstOrDefault();

        try
        {
            if (string.IsNullOrEmpty(orgId))
            {
                logger.LogWarning(
                    "SECURITY: Unauthorized attempt to fetch DORA metrics - OrgId missing, " +
                    "UserId: {UserId}, CorrelationId: {CorrelationId}",
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(userId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(correlationId));
                return Unauthorized(new { error = "Organization context not found" });
            }

            if (string.IsNullOrWhiteSpace(projectId))
            {
                logger.LogWarning(
                    "AUDIT: Invalid projectId in DORA metrics request - OrgId: {OrgId}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(userId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(correlationId));
                return BadRequest(new { error = "projectId is required" });
            }

            logger.LogInformation(
                "AUDIT: Fetching latest DORA metrics - OrgId: {OrgId}, ProjectId: {ProjectId}, " +
                "RepositoryName: {RepositoryName}, TeamName: {TeamName}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(repositoryName ?? "(all)"), Velo.Api.Logging.LogSanitizer.SanitiseForLog(teamName ?? "(all)"), Velo.Api.Logging.LogSanitizer.SanitiseForLog(userId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(correlationId));

            // Resolve the effective storage key:
            //  • repositoryName trumps teamName.
            //  • teamName is resolved to its single mapped repo, or to "team:<TeamName>"
            //    when it maps to multiple repos; zero mappings → empty-state response.
            string? filterKey;
            try
            {
                filterKey = await ResolveFilterKeyAsync(orgId, projectId, repositoryName, teamName, cancellationToken);
            }
            catch (TeamHasNoMappingsException)
            {
                return Ok(new DoraMetricsResponse
                {
                    Status = "empty",
                    Note = "Team has no mapped pipelines",
                    OrgId = orgId,
                    ProjectId = projectId,
                    TeamName = teamName
                });
            }

            var metrics = await metricsRepository.GetLatestAsync(orgId, projectId, filterKey, cancellationToken);

            if (metrics == null)
            {
                logger.LogInformation(
                    "AUDIT: No metrics found - OrgId: {OrgId}, ProjectId: {ProjectId}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(userId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(correlationId));

                // AUTO-RECOVERY: If the ADO token is present, kick off a background sync
                // for this specific project. This handles:
                //   1. First-time load before any webhook has fired
                //   2. Missed webhook events (delivery failures from ADO)
                //   3. Any gap in data that left this project without metrics
                if (!string.IsNullOrEmpty(adoToken))
                {
                    var capturedOrgId = orgId;
                    var capturedProjectId = projectId;
                    var capturedToken = adoToken;
                    var capturedRepo = repositoryName;
                    var capturedTeam = teamName;

                    // IMPORTANT: spawn the background work on a NEW DI scope.
                    // The request scope is disposed the moment this action returns, so
                    // `ingestService` / `doraComputeService` captured from the controller
                    // would hit ObjectDisposedException on their first DB/HttpClient call.
                    // Resolving fresh from a new IServiceScope avoids that.
                    _ = Task.Run(async () =>
                    {
                        await using var scope = scopeFactory.CreateAsyncScope();
                        var sp = scope.ServiceProvider;
                        var scopedIngest = sp.GetRequiredService<IAdoPipelineIngestService>();
                        var scopedCompute = sp.GetRequiredService<IDoraComputeService>();
                        var scopedDb = sp.GetRequiredService<Velo.SQL.VeloDbContext>();

                        try
                        {
                            // Set EF query-filter org AND SQL Server SESSION_CONTEXT(N'org_id')
                            // so RLS lets the background ingest/compute path read & write.
                            await TenantContextHelper.SetAsync(scopedDb, capturedOrgId, CancellationToken.None);

                            logger.LogInformation(
                                "AUTO_RECOVERY: Background sync triggered from dora/latest — OrgId={OrgId}, ProjectId={ProjectId}, " +
                                "RepositoryName={RepositoryName}, TeamName={TeamName}",
                                Velo.Api.Logging.LogSanitizer.SanitiseForLog(capturedOrgId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(capturedProjectId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(capturedRepo ?? "(all)"), Velo.Api.Logging.LogSanitizer.SanitiseForLog(capturedTeam ?? "(all)"));

                            var ingested = await scopedIngest.IngestAsync(
                                capturedOrgId, capturedProjectId, capturedToken, CancellationToken.None);

                            // Always recompute — even when nothing new was ingested, the
                            // background sync may have run AFTER a webhook delivered the latest
                            // build, in which case the existing rows still need a fresh metrics
                            // snapshot for the dashboard to render.
                            // Recompute the project-wide snapshot first so the dashboard's default
                            // view picks up the newly ingested runs, then — when filters were on
                            // the request — compute the filtered snapshot on top.
                            await scopedCompute.ComputeAndSaveAsync(
                                capturedOrgId, capturedProjectId, repositoryName: null, teamName: null, CancellationToken.None);

                            if (!string.IsNullOrWhiteSpace(capturedRepo) || !string.IsNullOrWhiteSpace(capturedTeam))
                            {
                                try
                                {
                                    await scopedCompute.ComputeAndSaveAsync(
                                        capturedOrgId, capturedProjectId, capturedRepo, capturedTeam, CancellationToken.None);
                                }
                                catch (TeamHasNoMappingsException)
                                {
                                    // Team has no mappings — surfaced to the user on the next poll
                                    // via ResolveFilterKeyAsync. Nothing to do here.
                                }
                            }

                            logger.LogInformation(
                                "AUTO_RECOVERY: Done — {Ingested} runs ingested, OrgId={OrgId}, ProjectId={ProjectId}",
                                ingested, Velo.Api.Logging.LogSanitizer.SanitiseForLog(capturedOrgId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(capturedProjectId));
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex,
                                "AUTO_RECOVERY: Background sync failed — OrgId={OrgId}, ProjectId={ProjectId}",
                                Velo.Api.Logging.LogSanitizer.SanitiseForLog(capturedOrgId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(capturedProjectId));
                        }
                    });

                    // Return "syncing" so the UI can show a progress indicator and poll
                    return Ok(new DoraMetricsResponse
                    {
                        Status = "syncing",
                        Message = "Syncing your pipeline history — metrics will appear in a few seconds.",
                        OrgId = orgId,
                        ProjectId = projectId,
                        RepositoryName = repositoryName,
                        TeamName = teamName
                    });
                }

                // Return 200 with a status flag instead of 404 so the UI can show
                // a friendly "gathering data" message rather than an error.
                return Ok(new DoraMetricsResponse
                {
                    Status = "gathering",
                    Message = "Successfully connected! We are gathering your pipeline data. Metrics will appear after your next pipeline run.",
                    OrgId = orgId,
                    ProjectId = projectId,
                    RepositoryName = repositoryName,
                    TeamName = teamName
                });
            }

            logger.LogInformation(
                "AUDIT: Successfully returned DORA metrics - OrgId: {OrgId}, ProjectId: {ProjectId}, " +
                "DeploymentFrequency: {DeploymentFrequency}, Rating: {Rating}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId), metrics.DeploymentFrequency, metrics.DeploymentFrequencyRating, Velo.Api.Logging.LogSanitizer.SanitiseForLog(userId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(correlationId));

            return Ok(new DoraMetricsResponse
            {
                Status = "ok",
                OrgId = orgId,
                ProjectId = projectId,
                RepositoryName = repositoryName,
                TeamName = teamName,
                Metrics = metrics
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex,
                "SECURITY: Unauthorized access to DORA metrics - OrgId: {OrgId}, ProjectId: {ProjectId}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(userId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(correlationId));
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "ERROR: Exception fetching DORA metrics - OrgId: {OrgId}, ProjectId: {ProjectId}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(userId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(correlationId));
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Get DORA metrics history for a project.
    /// Multi-tenant: Only returns metrics for the authenticated org_id.
    /// Enforced by: EF Core global query filter + SQL Server RLS.
    /// </summary>
    [HttpGet("history")]
    public async Task<ActionResult<DoraMetricsHistoryResponse>> GetMetricsHistory(
        [FromQuery] string projectId,
        [FromQuery] int days = 30,
        [FromQuery] string? repositoryName = null,
        [FromQuery] string? teamName = null,
        CancellationToken cancellationToken = default)
    {
        var orgId = HttpContext.Items["OrgId"]?.ToString();
        var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? "N/A";
        var userId = User?.FindFirst("sub")?.Value ?? "unknown";

        try
        {
            if (string.IsNullOrEmpty(orgId))
            {
                logger.LogWarning(
                    "SECURITY: Unauthorized attempt to fetch DORA history - OrgId missing, " +
                    "UserId: {UserId}, CorrelationId: {CorrelationId}",
                    Velo.Api.Logging.LogSanitizer.SanitiseForLog(userId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(correlationId));
                return Unauthorized(new { error = "Organization context not found" });
            }

            if (string.IsNullOrWhiteSpace(projectId))
            {
                return BadRequest(new { error = "projectId is required" });
            }

            if (days < 1 || days > 365)
            {
                return BadRequest(new { error = "days must be between 1 and 365" });
            }

            logger.LogInformation(
                "AUDIT: Fetching DORA metrics history - OrgId: {OrgId}, ProjectId: {ProjectId}, Days: {Days}, " +
                "RepositoryName: {RepositoryName}, TeamName: {TeamName}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId), days, Velo.Api.Logging.LogSanitizer.SanitiseForLog(repositoryName ?? "(all)"), Velo.Api.Logging.LogSanitizer.SanitiseForLog(teamName ?? "(all)"), Velo.Api.Logging.LogSanitizer.SanitiseForLog(userId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(correlationId));

            var from = DateTimeOffset.UtcNow.AddDays(-days);
            var to = DateTimeOffset.UtcNow;

            string? filterKey;
            try
            {
                filterKey = await ResolveFilterKeyAsync(orgId, projectId, repositoryName, teamName, cancellationToken);
            }
            catch (TeamHasNoMappingsException)
            {
                return Ok(new DoraMetricsHistoryResponse
                {
                    Status = "empty",
                    Note = "Team has no mapped pipelines",
                    OrgId = orgId,
                    ProjectId = projectId,
                    TeamName = teamName,
                    Days = days,
                    History = []
                });
            }

            var metrics = (await metricsRepository.GetHistoryAsync(orgId, projectId, from, to, filterKey, cancellationToken)).ToList();

            logger.LogInformation(
                "AUDIT: Successfully returned {MetricsCount} historical DORA metrics - OrgId: {OrgId}, ProjectId: {ProjectId}, Days: {Days}, " +
                "RepositoryName: {RepositoryName}, TeamName: {TeamName}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                metrics.Count, Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId), days, Velo.Api.Logging.LogSanitizer.SanitiseForLog(repositoryName ?? "(all)"), Velo.Api.Logging.LogSanitizer.SanitiseForLog(teamName ?? "(all)"), Velo.Api.Logging.LogSanitizer.SanitiseForLog(userId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(correlationId));

            return Ok(new DoraMetricsHistoryResponse
            {
                Status = "ok",
                OrgId = orgId,
                ProjectId = projectId,
                RepositoryName = repositoryName,
                TeamName = teamName,
                Days = days,
                History = metrics
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "ERROR: Exception fetching DORA metrics history - OrgId: {OrgId}, ProjectId: {ProjectId}, Days: {Days}, UserId: {UserId}, CorrelationId: {CorrelationId}",
                Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId), days, Velo.Api.Logging.LogSanitizer.SanitiseForLog(userId), Velo.Api.Logging.LogSanitizer.SanitiseForLog(correlationId));
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Mirrors the resolution logic in DoraComputeService.ResolveFilterAsync so the
    /// controller can find the right snapshot row.
    ///   • repositoryName supplied → returns the repo name (single-repo snapshot key).
    ///   • teamName supplied → looks up TeamMappings; returns the single mapped repo
    ///     when N=1, or "team:&lt;TeamName&gt;" when N&gt;1. Throws
    ///     <see cref="TeamHasNoMappingsException"/> when N=0.
    ///   • neither supplied → returns null (caller treats as project-wide aggregate,
    ///     which the repository maps to the "" sentinel).
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
