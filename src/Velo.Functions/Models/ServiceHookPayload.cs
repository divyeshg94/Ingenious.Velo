using System.Text.Json;
using System.Text.Json.Serialization;

namespace Velo.Functions.Models;

/// <summary>
/// Represents the payload sent by Azure DevOps service hooks.
/// Shape follows the ADO service hook schema:
/// https://learn.microsoft.com/azure/devops/service-hooks/events
/// </summary>
public class ServiceHookPayload
{
    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("publisherId")]
    public string PublisherId { get; set; } = string.Empty;

    [JsonPropertyName("resource")]
    public JsonElement? Resource { get; set; }

    [JsonPropertyName("resourceContainers")]
    public ResourceContainers? ResourceContainers { get; set; }

    [JsonPropertyName("createdDate")]
    public DateTimeOffset CreatedDate { get; set; }
}

public class ResourceContainers
{
    [JsonPropertyName("collection")]
    public ContainerRef? Collection { get; set; }

    [JsonPropertyName("account")]
    public ContainerRef? Account { get; set; }

    [JsonPropertyName("project")]
    public ContainerRef? Project { get; set; }
}

public class ContainerRef
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}
