namespace A2.Server.Models;

/// <summary>The outcome of one Activity within a Run, carried by an AppendActivityResult status update.
///
/// <see cref="ScenarioId"/> and <see cref="ActivityId"/> are pointers into the RUN'S OWN snapshot --
/// <c>RunScenarioSnapshot.Source.Id</c> and <c>RunActivitySnapshot.Source.Id</c> -- not references to the
/// live Scenario or Activity. They say which of this Run's own snapshot rows the result belongs to.
/// They MUST NOT be used to look up the live Scenario or Activity: those may since have been edited or
/// deleted, and the pointer is meaningful only against the snapshot taken when this Run was created.</summary>
public record ActivityResult
{
    public required Guid ScenarioId { get; init; }
    public required Guid ActivityId { get; init; }
    public required ActivityResultStatus Status { get; init; }

    public IReadOnlyDictionary<string, string> ResolvedPreconditions { get; init; } =
        new Dictionary<string, string>();

    public IReadOnlyDictionary<string, string> Evidence { get; init; } =
        new Dictionary<string, string>();

    /// <summary>Why this Activity was chosen for execution despite an earlier Activity failing, so a
    /// reader can understand the execution agent's behavior.</summary>
    public string? ContinuationReasoning { get; init; }
}

/// <summary>What became of an Activity by the time its <see cref="ActivityResult"/> was appended to the
/// Run's status update log. A result is only ever appended once its Activity has already concluded, so on
/// every row in the log the status is one of <see cref="Passed"/>, <see cref="Failed"/> or
/// <see cref="Skipped"/>. Pending and Running name the states before that point and should never appear on
/// an appended result.</summary>
public enum ActivityResultStatus
{
    Pending,
    Running,
    Passed,
    Failed,
    Skipped,
}
