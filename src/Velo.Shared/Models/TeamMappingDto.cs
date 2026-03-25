namespace Velo.Shared.Models;

public class TeamMappingDto
{
    public Guid Id { get; set; }
    public string OrgId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string RepositoryName { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
}
