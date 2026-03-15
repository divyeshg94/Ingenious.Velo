using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using Velo.Functions.Models;
using Velo.Functions.Services;

namespace Velo.Functions.Triggers;

/// <summary>
/// HTTP trigger that receives Azure DevOps service hook payloads.
/// Configure in ADO: Project Settings > Service Hooks > Azure Functions.
/// Events: Build completed, Release deployment completed, Pull request merged, Work item updated.
/// </summary>
public class ServiceHookTrigger(IEventNormalizer normalizer, ILogger<ServiceHookTrigger> logger)
{
    [Function(nameof(ServiceHookTrigger))]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "hooks/ado")] HttpRequestData req,
        FunctionContext context)
    {
        ServiceHookPayload? payload;
        try
        {
            payload = await req.ReadFromJsonAsync<ServiceHookPayload>();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to deserialize service hook payload");
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }

        if (payload is null)
        {
            return req.CreateResponse(HttpStatusCode.BadRequest);
        }

        logger.LogInformation("Received ADO service hook: EventType={EventType} OrgId={OrgId}",
            payload.EventType, payload.ResourceContainers?.Collection?.Id);

        await normalizer.NormalizeAndPersistAsync(payload);

        return req.CreateResponse(HttpStatusCode.OK);
    }
}
