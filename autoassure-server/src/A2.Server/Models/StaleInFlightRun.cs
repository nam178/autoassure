namespace A2.Server.Models;

/// <summary>Identifies one in-flight Run the sweeper should act on -- its heartbeat has gone stale, its
/// deadline has passed, or both. Carries only what a caller needs to call End Run on it, not the rest of
/// the Run: the fast lookup this comes from only answers "which Run", not "what is this Run", so a caller
/// that needs the full picture makes its own follow-up Get Run.</summary>
public record StaleInFlightRun
{
    public required Guid OrganizationId { get; init; }
    public required Guid ApplicationId { get; init; }
    public required Guid Id { get; init; }
}
