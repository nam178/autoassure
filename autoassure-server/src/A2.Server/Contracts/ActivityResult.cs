using System.ComponentModel.DataAnnotations;

namespace A2.Server.Contracts;

/// <summary>
///     The outcome of one Activity within a Run, as sent by a worker appending a
///     status update and
///     as returned back to the client reading the log. ScenarioId and ActivityId
///     identify rows in the Run's
///     OWN snapshot -- the ids the Get Run response's Scenario/Activity snapshots
///     carry on their Source --
///     not the live Scenario or Activity, which may since have changed or been
///     deleted.
/// </summary>
public record ActivityResult
{
    public required Guid ScenarioId { get; init; }
    public required Guid ActivityId { get; init; }

    [EnumDataType(typeof(ActivityResultStatus))]
    public required ActivityResultStatus Status { get; init; }

    [MaxLength(50)]
    public IReadOnlyDictionary<
        Guid,
        string
    >? ResolvedPreconditions { get; init; }

    [MaxLength(50)]
    public IReadOnlyDictionary<Guid, string>? Evidence { get; init; }

    /// <summary>
    ///     Why this Activity was chosen for execution despite an earlier Activity
    ///     failing.
    /// </summary>
    [MaxLength(2000)]
    public string? ContinuationReasoning { get; init; }
}