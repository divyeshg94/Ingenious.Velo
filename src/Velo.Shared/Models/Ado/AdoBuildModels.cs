namespace Velo.Shared.Models.Ado;

/// <summary>
/// Represents the list response from the Azure DevOps Builds REST API.
/// GET {org}/{project}/_apis/build/builds
/// </summary>
public record AdoBuildsResponse(AdoBuild[] Value, int Count);

public record AdoBuild(
    int Id,
    string? BuildNumber,
    string? Result,
    string? Status,
    DateTimeOffset? StartTime,
    DateTimeOffset? FinishTime,
    AdoDefinition? Definition,
    AdoIdentity? RequestedBy);

/// <summary>
/// Represents the root payload sent by an Azure DevOps service hook (build.complete event).
/// </summary>
public record AdoBuildCompleteEvent(
    string? EventType,
    AdoBuildResource? Resource,
    AdoResourceContainers? ResourceContainers);

/// <summary>
/// The Build object embedded in the build.complete service hook payload.
/// Url is included as a reliable fallback for extracting project name when
/// resource.project is absent (some ADO versions omit it from service hook payloads).
/// </summary>
public record AdoBuildResource(
    int Id,
    string? BuildNumber,
    string? Result,
    string? Status,
    DateTimeOffset? StartTime,
    DateTimeOffset? FinishTime,
    string? Url,
    AdoDefinition? Definition,
    AdoProject? Project,
    AdoIdentity? RequestedBy);

public record AdoDefinition(int Id, string? Name);
public record AdoProject(string? Id, string? Name);
public record AdoIdentity(string? DisplayName);
public record AdoResourceContainers(AdoAccount? Account, AdoAccount? Collection, AdoAccount? Project);
public record AdoAccount(string? Id, string? BaseUrl);
