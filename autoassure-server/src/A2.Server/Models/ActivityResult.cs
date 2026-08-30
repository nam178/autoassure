namespace A2.Server.Models;

/// <summary>The outcome of one Activity within a Run, embedded and populated as execution proceeds
/// (empty at creation, since execution itself is stubbed for now).</summary>
public record ActivityResult
{
    public required Guid ScenarioId { get; init; }
    public required Guid ActivityId { get; init; }
    public required ActivityResultStatus Status { get; init; }

    public IReadOnlyDictionary<string, string> ResolvedPreconditions { get; init; } =
        new Dictionary<string, string>();

    public IReadOnlyDictionary<string, string> Evidence { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// The reason why this activity was chosen for execution, despite previous activities failed.
    /// This helps customer understand the execution agent's behavior.
    /// </summary>
    public string? ContinuationReasoning { get; init; }
}

/// <summary>The outcome of a single Activity within a Run.</summary>
public enum ActivityResultStatus
{
    Pending,
    Running,
    Passed,
    Failed,
    Skipped,
}
