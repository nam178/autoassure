namespace A2.Server.Repositories;

/// <summary>
///     The only Run fields <see cref="IRunRepository.TryUpdateAsync" /> is allowed
///     to change. A null
///     field is left unchanged; set a field to write it. At least one field must
///     be set.
/// </summary>
public record RunUpdatableFields
{
    /// <summary>
    ///     Proves the owning worker is still alive by overwriting
    ///     <c>LastHeartbeatAt</c>. This is the
    ///     one Run attribute that is never a permanent record -- it is overwritten in
    ///     place on every beat,
    ///     and a beat is worth nothing once the next one arrives.
    /// </summary>
    public DateTimeOffset? HeartbeatAt { get; init; }

    /// <summary>
    ///     Overwrites the Run's four activity counts with the absolute values supplied
    ///     here -- never
    ///     an increment -- so a retried call does no harm. Set together with
    ///     <see cref="PassedActivityCount" />, <see cref="FailedActivityCount" /> and
    ///     <see cref="SkippedActivityCount" />; nothing that needs exact progress
    ///     reads these counts, since
    ///     that caller folds the status update log instead.
    /// </summary>
    public int? TotalActivityCount { get; init; }

    public int? PassedActivityCount { get; init; }

    public int? FailedActivityCount { get; init; }

    public int? SkippedActivityCount { get; init; }
}