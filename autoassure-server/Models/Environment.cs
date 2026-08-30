namespace A2.Server.Models;

/// <summary>A deployment target (e.g. "Staging", "Production") that a Try/Run executes against.
/// Variables live separately — see <see cref="EnvironmentVariable"/>.</summary>
public record Environment
{
    public required Guid Id { get; init; }
    public required Guid OrganizationId { get; init; }
    public required Guid ApplicationId { get; init; }
    public required string Name { get; init; }
    public required EnvironmentClassification Classification { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required Guid UpdatedByUserId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Whether an Environment is a live Production system or a non-production one (staging, dev, ...).</summary>
public enum EnvironmentClassification
{
    Production,
    NonProduction,
}
