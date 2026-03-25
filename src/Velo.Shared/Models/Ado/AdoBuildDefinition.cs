namespace Velo.Shared.Models.Ado;

/// <summary>
/// Response from GET {org}/{project}/_apis/build/definitions/{id}?api-version=7.1
/// Used to resolve which repository a pipeline is associated with.
/// </summary>
public record AdoBuildDefinition(
    int Id,
    string? Name,
    AdoBuildDefinitionRepository? Repository);

public record AdoBuildDefinitionRepository(
    string Id,
    string Name,
    string? Type);
