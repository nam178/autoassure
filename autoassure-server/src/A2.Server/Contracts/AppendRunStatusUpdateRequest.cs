using System.ComponentModel.DataAnnotations;

namespace A2.Server.Contracts;

/// <summary>
///     Request body to append one entry to a Run's status update log, at the
///     caller's own sequence
///     number. The owning worker allocates Seq in memory -- this API never invents
///     one. Sequence numbers are
///     1-based and dense: the first update of a Run's log has Seq 1.
///     AppendActivityResult is the only kind of status update that exists today,
///     so this always carries an
///     ActivityResult.
/// </summary>
public record AppendRunStatusUpdateRequest
{
    [Range(1, long.MaxValue)] public required long Seq { get; init; }

    public required ActivityResult ActivityResult { get; init; }
}