namespace A2.Server.Models;

/// <summary>A system under test that Scenarios, Environments, and libraries (Preconditions,
/// EvidenceDefinitions) attach to, owned by an Organization.</summary>
public record Application
{
    public required Guid Id { get; init; }
    public required Guid OrganizationId { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required Guid UpdatedByUserId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
