namespace Velo.Shared.Models;

public class TeamMappingDto
{
    /// <summary>
    /// Leave as <see cref="Guid.Empty"/> (or omit) when creating a new mapping;
    /// the server will assign a new ID.
    /// </summary>
    public Guid Id { get; set; } = Guid.Empty;
    public string OrgId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string RepositoryName { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
}
