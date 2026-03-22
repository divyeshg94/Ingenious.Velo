namespace Velo.Shared.Models.Ado;

/// <summary>
/// Root payload for git.pullrequest.created and git.pullrequest.updated service hook events.
/// </summary>
public record AdoPrEvent(
    string? EventType,
    AdoPrResource? Resource,
    AdoResourceContainers? ResourceContainers);

/// <summary>
/// The Pull Request object embedded in the ADO PR service hook payload.
/// </summary>
public record AdoPrResource(
    int PullRequestId,
    string? Title,
    /// <summary>active | completed | abandoned</summary>
    string? Status,
    string? SourceRefName,
    string? TargetRefName,
    DateTimeOffset CreationDate,
    DateTimeOffset? ClosedDate,
    AdoIdentity? CreatedBy,
    AdoPrReviewer[]? Reviewers,
    AdoPrRepository? Repository);

/// <summary>A reviewer entry; Vote >= 10 means approved.</summary>
public record AdoPrReviewer(string? DisplayName, int Vote);

public record AdoPrRepository(AdoPrProject? Project);
public record AdoPrProject(string? Id, string? Name);
