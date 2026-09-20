namespace A2.Server.Models;

/// <summary>
///     A test case for an Application: a freeform description of what to verify,
///     organized by
///     Folder/Tags. Its ordered steps live as separate Activity entities scoped to
///     this Scenario.
/// </summary>
public record Scenario
{
    public required Guid Id { get; init; }
    public required Guid OrganizationId { get; init; }
    public required Guid ApplicationId { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Folder { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Number of Activities currently in this Scenario.</summary>
    public int ActivityCount { get; init; }

    public required Guid CreatedByUserId { get; init; }
    public required Guid UpdatedByUserId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}