namespace A2.Server.Models;

public record RunStatusUpdate
{
    public required long Seq { get; init; }
    public required RunStatusUpdateKind Kind { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Exists when Kind = RunStatusUpdateKind.AppendActivityResult
    /// </summary>
    public ActivityResult? ActivityResult { get; init; }
}

public enum RunStatusUpdateKind
{
    AppendActivityResult,
}

/// <summary>The outcome of one Activity within a Run.</summary>
public record ActivityResult
{
    public required Guid ScenarioId { get; init; }
    public required Guid ActivityId { get; init; }
    public required ActivityResultStatus Status { get; init; }

    /// <summary>Keyed by Precondition Id -- not Name, since two Preconditions can share a Name.</summary>
    public IReadOnlyDictionary<Guid, string> ResolvedPreconditions { get; init; } =
        new Dictionary<Guid, string>();

    /// <summary>Keyed by EvidenceDefinition Id -- not Name, since two EvidenceDefinitions can share a
    /// Name.</summary>
    public IReadOnlyDictionary<Guid, string> Evidence { get; init; } =
        new Dictionary<Guid, string>();

    /// <summary>Why this Activity was chosen for execution despite an earlier Activity failing, so a
    /// reader can understand the execution agent's behavior.</summary>
    public string? ContinuationReasoning { get; init; }
}

public enum ActivityResultStatus
{
    Pending,
    Running,
    Passed,
    Failed,
    Skipped,
}
