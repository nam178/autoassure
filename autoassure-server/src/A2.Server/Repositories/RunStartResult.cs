namespace A2.Server.Repositories;

/// <summary>
///     The values <see cref="IRunRepository.TryMarkAsStartedAsync" /> actually
///     wrote onto a Run's header when
///     its claim won, handed back so a caller can build a response that reflects
///     what was persisted instead
///     of recomputing the same derivation a second time.
/// </summary>
public record RunStartResult
{
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset LastHeartbeatAt { get; init; }
}
