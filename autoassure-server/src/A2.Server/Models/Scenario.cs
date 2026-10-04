namespace A2.Server.Models;

/// <summary>
/// A test case for an Application: a freeform description of what to verify,
/// organized by
/// Folder/Tags. Its ordered steps live as separate Activity entities scoped to
/// this Scenario.
/// </summary>
public record Scenario
{
    public required Guid Id { get; init; }
    public required Guid OrganizationId { get; init; }
    public required Guid ApplicationId { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Folder { get; init; }

    /// <summary>
    /// User-defined tags for organizing and filtering scenarios.
    /// At most <see cref="A2.Server.Common.Quota.MaxTagsPerScenario"/> tags per
    /// scenario, no duplicates, case-sensitive comparison.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Number of Activities currently in this Scenario.</summary>
    public int ActivityCount { get; init; }

    /// <summary>
    /// The lifecycle state of this Scenario. A Scenario only ever holds
    /// <see cref="LifecycleState.Active" /> or
    /// <see cref="LifecycleState.Archived" />; it is deleted in a single step,
    /// so <see cref="LifecycleState.Deleting" /> does not apply.
    /// </summary>
    public LifecycleState LifecycleState { get; init; } = LifecycleState.Active;

    public required Guid CreatedByUserId { get; init; }
    public required Guid UpdatedByUserId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
