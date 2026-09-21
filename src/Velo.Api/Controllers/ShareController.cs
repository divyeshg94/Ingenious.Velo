using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Velo.Api.Helpers;
using Velo.Api.Services;
using Velo.Shared.Contracts;
using Velo.Shared.Models;
using Velo.SQL;
using Velo.SQL.Models;

namespace Velo.Api.Controllers;

/// <summary>
/// "Share dashboard" — lets a team hand a read-only DORA snapshot to people who don't
/// have Azure DevOps access (e.g. leadership). Growth rationale: teams sharing results
/// externally are a distribution channel that brings new orgs back to Velo.
/// SECURITY: the create/revoke endpoints require auth and are org-scoped; the public
/// viewer endpoint is intentionally anonymous (that's the point of a share link) and
/// serves a snapshot taken at creation time, never a live query, so a shared link can
/// never be used to pivot into the sharing org's live/current data.
/// </summary>
[ApiController]
[Route("api/share")]
[Authorize]
public class ShareController(
    VeloDbContext db,
    IMetricsRepository metricsRepository,
    IOnboardingService onboardingService,
    ILogger<ShareController> logger) : ControllerBase
{
    private static readonly TimeSpan LinkLifetime = TimeSpan.FromDays(30);

    [HttpPost("dora")]
    public async Task<ActionResult<CreateShareResponse>> CreateDoraShare(
        [FromBody] CreateShareRequest request, CancellationToken cancellationToken)
    {
        var orgId = HttpContext.Items["OrgId"]?.ToString();
        if (string.IsNullOrEmpty(orgId))
            return Unauthorized(new { error = "Organization context not found" });

        if (string.IsNullOrWhiteSpace(request.ProjectId))
            return BadRequest(new { error = "projectId is required" });

        var org = await metricsRepository.GetOrgContextAsync(orgId, cancellationToken);
        var metrics = await metricsRepository.GetLatestAsync(orgId, request.ProjectId, request.RepositoryName, cancellationToken);
        if (metrics == null)
            return NotFound(new { error = "No DORA metrics available yet for this project/filter." });

        var report = new SharedReport
        {
            OrgId = orgId,
            OrgDisplayName = org?.DisplayName ?? orgId,
            ProjectId = request.ProjectId,
            RepositoryName = request.RepositoryName,
            MetricsSnapshotJson = JsonSerializer.Serialize(metrics),
            CreatedBy = UserIdentityResolver.ResolveUserIdentifier(User),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.Add(LinkLifetime)
        };

        db.SharedReports.Add(report);
        await db.SaveChangesAsync(cancellationToken);

        await onboardingService.MarkShareCreatedAsync(orgId, cancellationToken);

        logger.LogInformation("AUDIT: Created shared DORA report - OrgId: {OrgId}, ShareId: {ShareId}",
            Velo.Api.Logging.LogSanitizer.SanitiseForLog(orgId), report.Id);

        var shareUrl = $"{Request.Scheme}://{Request.Host}/api/share/dora/{report.Id}";
        return Ok(new CreateShareResponse(report.Id, shareUrl, report.ExpiresAt));
    }

    [HttpPost("dora/{id:guid}/revoke")]
    public async Task<IActionResult> RevokeDoraShare(Guid id, CancellationToken cancellationToken)
    {
        var orgId = HttpContext.Items["OrgId"]?.ToString();
        if (string.IsNullOrEmpty(orgId))
            return Unauthorized(new { error = "Organization context not found" });

        var report = await db.SharedReports.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (report == null || report.OrgId != orgId)
            return NotFound(new { error = "Share link not found." });

        report.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Share link revoked." });
    }

    /// <summary>
    /// Public, unauthenticated viewer — this is the link that gets forwarded to
    /// leadership. Renders a small standalone HTML page rather than JSON since the
    /// recipient has no Angular app / ADO context to render it in.
    /// </summary>
    [HttpGet("dora/{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> ViewDoraShare(Guid id, CancellationToken cancellationToken)
    {
        var report = await db.SharedReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (report == null || report.RevokedAt != null || report.ExpiresAt < DateTimeOffset.UtcNow)
        {
            return Content(RenderErrorPage("This share link has expired or is no longer available."), "text/html");
        }

        DoraMetricsDto? metrics;
        try
        {
            metrics = JsonSerializer.Deserialize<DoraMetricsDto>(report.MetricsSnapshotJson);
        }
        catch (JsonException)
        {
            metrics = null;
        }

        if (metrics == null)
            return Content(RenderErrorPage("This share link's data could not be loaded."), "text/html");

        return Content(RenderReportPage(report, metrics), "text/html");
    }

    private static string RenderErrorPage(string message) => $@"
<html><head><meta charset=""utf-8""><title>Velo — Shared Report</title></head>
<body style=""font-family:Segoe UI,Arial,sans-serif;padding:60px;text-align:center;color:#333;"">
<h2>{WebUtility.HtmlEncode(message)}</h2>
</body></html>";

    private static string RenderReportPage(SharedReport report, DoraMetricsDto m)
    {
        string Row(string label, string value, string rating) => $@"
<tr>
  <td style=""padding:12px 16px;border-bottom:1px solid #eee;"">{WebUtility.HtmlEncode(label)}</td>
  <td style=""padding:12px 16px;border-bottom:1px solid #eee;font-weight:600;"">{WebUtility.HtmlEncode(value)}</td>
  <td style=""padding:12px 16px;border-bottom:1px solid #eee;"">
    <span style=""background:{RatingColor(rating)};color:#fff;padding:2px 10px;border-radius:10px;font-size:12px;"">{WebUtility.HtmlEncode(rating)}</span>
  </td>
</tr>";

        var rows = string.Concat(
            Row("Deployment Frequency", $"{m.DeploymentFrequency:0.0}/day", m.DeploymentFrequencyRating),
            Row("Lead Time for Changes", $"{m.LeadTimeForChangesHours:0.0} hrs", m.LeadTimeRating),
            Row("Change Failure Rate", $"{m.ChangeFailureRate:0.0}%", m.ChangeFailureRating),
            Row("Mean Time to Restore", $"{m.MeanTimeToRestoreHours:0.0} hrs", m.MttrRating),
            Row("Rework Rate", $"{m.ReworkRate:0.0}%", m.ReworkRateRating));

        return $@"
<html>
<head><meta charset=""utf-8""><title>{WebUtility.HtmlEncode(report.OrgDisplayName)} — DORA Metrics — Velo</title></head>
<body style=""font-family:Segoe UI,Arial,sans-serif;background:#f5f5f5;margin:0;padding:40px 16px;color:#333;"">
  <div style=""max-width:640px;margin:0 auto;background:#fff;border-radius:8px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,0.08);"">
    <div style=""background:#0078d4;color:#fff;padding:24px 28px;"">
      <div style=""font-size:12px;letter-spacing:.05em;text-transform:uppercase;opacity:.85;"">Velo — DORA Metrics Snapshot</div>
      <h1 style=""margin:8px 0 0;font-size:22px;"">{WebUtility.HtmlEncode(report.OrgDisplayName)} · {WebUtility.HtmlEncode(report.ProjectId)}</h1>
    </div>
    <table style=""width:100%;border-collapse:collapse;"">
      {rows}
    </table>
    <div style=""padding:16px 28px;font-size:12px;color:#777;"">
      Generated {WebUtility.HtmlEncode(report.CreatedAt.ToString("MMM d, yyyy"))} · Shared via
      <a href=""https://marketplace.visualstudio.com/items?itemName=IngeniousLabs.velo"" style=""color:#0078d4;"">Velo</a>
      for Azure DevOps
    </div>
  </div>
</body>
</html>";
    }

    private static string RatingColor(string rating) => rating.ToLowerInvariant() switch
    {
        "elite" => "#22c55e",
        "high" => "#3b82f6",
        "medium" => "#f59e0b",
        _ => "#ef4444"
    };
}

public record CreateShareRequest(string ProjectId, string? RepositoryName = null);
public record CreateShareResponse(Guid Id, string ShareUrl, DateTimeOffset ExpiresAt);
