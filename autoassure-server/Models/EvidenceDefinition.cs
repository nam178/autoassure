namespace A2.Server.Models;

/// <summary>A reusable evidence definition in an Application's library (e.g. "Order Confirmation ID"
/// captured as output), referenced by an Activity's EvidenceIds across multiple Scenarios.</summary>
public record EvidenceDefinition
{
    public required Guid Id { get; init; }
    public required Guid OrganizationId { get; init; }
    public required Guid ApplicationId { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string ExampleValue { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required Guid UpdatedByUserId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
