namespace A2.Server.Models;

/// <summary>A reusable precondition definition in an Application's library (e.g. "Order Confirmation
/// ID"), referenced by an Activity's PreconditionIds across multiple Scenarios. An Activity
/// always reflects the current library definition -- no per-use override.</summary>
public record Precondition
{
    public required Guid Id { get; init; }
    public required Guid OrganizationId { get; init; }
    public required Guid ApplicationId { get; init; }
    public required string Name { get; init; }
    public required PreconditionValueSource ValueSource { get; init; }
    public required string ExampleValue { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required Guid UpdatedByUserId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Where a Precondition's value comes from at execution time.</summary>
public enum PreconditionValueSource
{
    PriorActivity,
    AskAtRunTime,
    SpecificValue,
}
