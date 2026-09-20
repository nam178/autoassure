namespace A2.Server.Models;

/// <summary>
///     Identifies one Run that is currently Running. Carries only what marks it as
///     such, not the
///     rest of the Run: a caller that needs the full picture makes its own
///     follow-up Get Run.
/// </summary>
public record RunningRun
{
    public required Guid Id { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
}
