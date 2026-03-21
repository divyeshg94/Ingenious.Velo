namespace Velo.Api.Models.Requests;

/// <summary>
/// Request body for registering or updating an Azure DevOps organisation connection.
/// </summary>
public record ConnectionConfig(string OrgUrl, string PersonalAccessToken);
