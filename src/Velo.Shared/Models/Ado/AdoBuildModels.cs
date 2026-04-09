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
    AdoIdentity? RequestedBy,
    AdoBuildRequest[]? Requests);

/// <summary>
/// Entry in the build.complete resource.requests array (legacy XAML build format).
/// Modern YAML builds populate resource.requestedBy at the top level instead.
/// </summary>
public record AdoBuildRequest(int Id, AdoIdentity? RequestedFor);
public record AdoDefinition(int Id, string? Name, string? Url);
public record AdoProject(string? Id, string? Name);
public record AdoIdentity(string? DisplayName);
public record AdoResourceContainers(AdoAccount? Account, AdoAccount? Collection, AdoAccount? Project);
public record AdoAccount(string? Id, string? BaseUrl);

/// <summary>
/// Response from GET {org}/_apis/projects — list of all ADO projects in the organisation.
/// </summary>
public record AdoProjectsResponse(AdoProjectRef[] Value, int Count);

public record AdoProjectRef(string Id, string Name);

/// <summary>
/// Response from GET {org}/{project}/_apis/git/repositories/{repoId}/pullrequests
/// Lists all PRs in a repository.
/// </summary>
public record AdoPullRequestsResponse(AdoPullRequest[] Value, int Count);

/// <summary>
/// Represents a pull request from the ADO Git REST API.
/// Includes diff statistics from the iterationDetails endpoint.
/// </summary>
public record AdoPullRequest(
    int PullRequestId,
    string? Title,
    string? Description,
    string? SourceRefName,
    string? TargetRefName,
    string? Status,
    DateTimeOffset? CreationDate,
    DateTimeOffset? ClosedDate,
    AdoIdentity? CreatedBy,
    AdoPullRequestReviewer[]? Reviewers,
    int? FilesChanged = null,
    int? LinesAdded = null,
    int? LinesDeleted = null);

/// <summary>
/// Represents a reviewer on a pull request.
/// Vote: 10 = approved, -10 = rejected, 5 = approved with suggestions, 0 = no vote
/// </summary>
public record AdoPullRequestReviewer(
    int Id,
    string? DisplayName,
    int Vote,
    bool IsContainer = false);

/// <summary>
/// Response from GET {org}/{project}/_apis/git/repositories/{repoId}/pullrequests/{prId}/iterations
/// Contains detailed diff statistics for a PR.
/// </summary>
public record AdoPullRequestIterationsResponse(AdoPullRequestIteration[] Value, int Count);

/// <summary>
/// Represents an iteration (version) of a PR.
/// The last iteration contains the final diff statistics.
/// </summary>
public record AdoPullRequestIteration(
    int Id,
    int Author,
    string? Author_DisplayName,
    DateTimeOffset? CreatedDate,
    AdoPullRequestIterationChanges? IterationChanges);

/// <summary>
/// Diff statistics for a PR iteration.
/// </summary>
public record AdoPullRequestIterationChanges(
    int? ChangeCountEdit = null,
    int? ChangeCountAdd = null,
    int? ChangeCountDelete = null,
    int? ChangeCountRename = null);
