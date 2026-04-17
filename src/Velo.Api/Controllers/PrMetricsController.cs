using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Velo.Api.Services;

namespace Velo.Api.Controllers;

[ApiController]
[Route("api/pr-metrics")]
[Authorize]
public class PrMetricsController(
    IPrSizeMetricsService prMetricsService,
    ILogger<PrMetricsController> logger) : ControllerBase
{
    /// <summary>
    /// GET /api/pr-metrics/average-size?projectId={projectId}&days={days}
    /// Returns average PR size metrics for a project over the specified period.
    /// Days defaults to 30.
    /// </summary>
    [HttpGet("average-size")]
    public async Task<ActionResult<PrSizeMetricsDto>> GetAveragePrSize(
        [FromQuery] string projectId,
        [FromQuery] int days = 30,
        CancellationToken cancellationToken = default)
    {
        var orgId = HttpContext.Items["OrgId"]?.ToString();
        if (string.IsNullOrEmpty(orgId))
            return Unauthorized(new { error = "Organization context not found" });

        if (string.IsNullOrEmpty(projectId))
            return BadRequest(new { error = "projectId is required" });

        if (days <= 0 || days > 365)
            return BadRequest(new { error = "days must be between 1 and 365" });

        var to = DateTimeOffset.UtcNow;
        var from = to.AddDays(-days);

        var metrics = await prMetricsService.GetAveragePrSizeAsync(orgId, projectId, from, to, cancellationToken);

        if (metrics == null)
            return StatusCode(500, new { error = "Failed to calculate PR metrics" });

        logger.LogInformation(
            "PR_METRICS_API: Retrieved for OrgId={OrgId}, ProjectId={ProjectId}, PRCount={Count}",
            Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId),
            Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId),
            metrics.TotalPrCount);

        return Ok(metrics);
    }

    /// <summary>
    /// GET /api/pr-metrics/distribution?projectId={projectId}&days={days}
    /// Returns PR size distribution (small/medium/large/extra-large buckets).
    /// </summary>
    [HttpGet("distribution")]
    public async Task<ActionResult<PrSizeDistributionDto>> GetPrSizeDistribution(
        [FromQuery] string projectId,
        [FromQuery] int days = 30,
        CancellationToken cancellationToken = default)
    {
        var orgId = HttpContext.Items["OrgId"]?.ToString();
        if (string.IsNullOrEmpty(orgId))
            return Unauthorized(new { error = "Organization context not found" });

        if (string.IsNullOrEmpty(projectId))
            return BadRequest(new { error = "projectId is required" });

        if (days <= 0 || days > 365)
            return BadRequest(new { error = "days must be between 1 and 365" });

        var to = DateTimeOffset.UtcNow;
        var from = to.AddDays(-days);

        var distribution = await prMetricsService.GetPrSizeDistributionAsync(orgId, projectId, from, to, cancellationToken);

        return Ok(distribution);
    }

    /// <summary>
    /// GET /api/pr-metrics/reviewers?projectId={projectId}&topCount={topCount}&days={days}
    /// Returns top reviewers by participation and approval patterns.
    /// topCount defaults to 10.
    /// </summary>
    [HttpGet("reviewers")]
    public async Task<ActionResult<IEnumerable<ReviewerInsightsDto>>> GetTopReviewers(
        [FromQuery] string projectId,
        [FromQuery] int topCount = 10,
        [FromQuery] int days = 30,
        CancellationToken cancellationToken = default)
    {
        var orgId = HttpContext.Items["OrgId"]?.ToString();
        if (string.IsNullOrEmpty(orgId))
            return Unauthorized(new { error = "Organization context not found" });

        if (string.IsNullOrEmpty(projectId))
            return BadRequest(new { error = "projectId is required" });

        if (days <= 0 || days > 365)
            return BadRequest(new { error = "days must be between 1 and 365" });

        if (topCount <= 0 || topCount > 100)
            return BadRequest(new { error = "topCount must be between 1 and 100" });

        var to = DateTimeOffset.UtcNow;
        var from = to.AddDays(-days);

        var reviewers = await prMetricsService.GetTopReviewersAsync(orgId, projectId, topCount, from, to, cancellationToken);

        logger.LogInformation(
            "PR_REVIEWERS_API: Retrieved {Count} reviewers for OrgId={OrgId}, ProjectId={ProjectId}",
            reviewers.Count(),
            Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId),
            Velo.Api.Logging.LogSanitizer.SanitiseForLog(projectId));

        return Ok(reviewers);
    }
}
