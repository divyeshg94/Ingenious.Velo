using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Velo.Api.Interface;
using Velo.Shared.Models;

namespace Velo.Api.Controllers;

public record AgentConfigTestRequest(
    string FoundryEndpoint,
    string? AgentId,
    // Auth option 1
    string? ApiKey,
    // Auth option 2
    string? TenantId,
    string? ClientId,
    string? ClientSecret);

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AgentConfigController(
    IAgentConfigService configService,
    ILogger<AgentConfigController> logger) : ControllerBase
{
    /// <summary>Returns the Foundry agent config for the authenticated org, or 404 if not configured.</summary>
    [HttpGet]
    public async Task<ActionResult<AgentConfigurationDto>> GetConfig(CancellationToken ct = default)
    {
        var orgId = HttpContext.Items["OrgId"]?.ToString();
        if (string.IsNullOrEmpty(orgId)) return Unauthorized();

        var config = await configService.GetConfigAsync(orgId, ct);
        if (config is null) return NotFound(new { status = "not_configured" });

        return Ok(config);
    }

    /// <summary>Creates or updates the Foundry agent config for the authenticated org.</summary>
    [HttpPost]
    public async Task<ActionResult<AgentConfigurationDto>> SaveConfig(
        [FromBody] AgentConfigurationDto dto,
        CancellationToken ct = default)
    {
        var orgId = HttpContext.Items["OrgId"]?.ToString();
        if (string.IsNullOrEmpty(orgId)) return Unauthorized();

        if (string.IsNullOrWhiteSpace(dto.FoundryEndpoint))
            return BadRequest(new { error = "foundryEndpoint is required" });

        // SSRF guard — only allow Azure AI Foundry / Azure ML endpoints.
        if (!IsAllowedFoundryEndpoint(dto.FoundryEndpoint))
            return BadRequest(new { error = "foundryEndpoint must be a valid Azure AI Foundry endpoint (*.services.ai.azure.com or *.api.azureml.ms)." });

        // AgentId is optional — Velo auto-creates the agent on first chat when not supplied

        var saved = await configService.SaveConfigAsync(orgId, dto, ct);
        logger.LogInformation("AGENT_CONFIG: Saved config for OrgId={OrgId}", orgId);

        return Ok(saved);
    }

    /// <summary>Removes the Foundry agent config for the authenticated org.</summary>
    [HttpDelete]
    public async Task<ActionResult> DeleteConfig(CancellationToken ct = default)
    {
        var orgId = HttpContext.Items["OrgId"]?.ToString();
        if (string.IsNullOrEmpty(orgId)) return Unauthorized();

        await configService.DeleteConfigAsync(orgId, ct);
        logger.LogInformation("AGENT_CONFIG: Deleted config for OrgId={OrgId}", orgId);

        return NoContent();
    }

    /// <summary>
    /// SSRF guard: only Azure AI Foundry project endpoints and Azure ML workspace endpoints.
    /// *.openai.azure.com is intentionally excluded — it is not compatible with the Agents API
    /// and users are guided to the correct endpoint type via error messages elsewhere.
    /// </summary>
    private static bool IsAllowedFoundryEndpoint(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return false;

        if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            return false;

        var host = uri.Host.ToLowerInvariant();
        return host.EndsWith(".services.ai.azure.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".api.azureml.ms",        StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Tests connectivity to the specified Foundry endpoint + agent ID.</summary>
    [HttpPost("test")]
    public async Task<ActionResult> TestConnection(
        [FromBody] AgentConfigTestRequest request,
        CancellationToken ct = default)
    {
        var orgId = HttpContext.Items["OrgId"]?.ToString();
        if (string.IsNullOrEmpty(orgId)) return Unauthorized();

        if (!IsAllowedFoundryEndpoint(request.FoundryEndpoint))
            return BadRequest(new { error = "foundryEndpoint must be a valid Azure AI Foundry endpoint." });

        var (ok, message) = await configService.TestConnectionAsync(
            request.FoundryEndpoint, request.AgentId,
            request.ApiKey,
            request.TenantId, request.ClientId, request.ClientSecret, ct);

        logger.LogInformation(
            "AGENT_CONFIG: Test connection OrgId={OrgId} — Result={Ok}", orgId, ok);

        if (ok)
            return Ok(new { status = "connected", message });

        return BadRequest(new { status = "failed", message });
    }
}
