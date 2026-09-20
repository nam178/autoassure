namespace A2.Server.Contracts;

/// <summary>
///     One entry of a Run's status update log, as returned to the client. The API
///     hands these back
///     exactly as appended, in sequence order -- it never folds or interprets
///     them; only the client
///     does.
/// </summary>
public record RunStatusUpdateResponse
{
    public required long Seq { get; init; }
    public required RunStatusUpdateKind Kind { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    ///     Set when Kind is AppendActivityResult -- the only kind today, so always set
    ///     in
    ///     practice.
    /// </summary>
    public ActivityResult? ActivityResult { get; init; }
}
