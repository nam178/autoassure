namespace A2.Server.Models;

// ReSharper disable UnusedAutoPropertyAccessor.Global -- audit fields, not yet read by any consumer
/// <summary>
///     A single key-value pair belonging to an Environment, managed independently
///     so setting or
///     removing one variable never affects the others.
/// </summary>
public record EnvironmentVariable
{
    public required Guid OrganizationId { get; init; }
    public required Guid EnvironmentId { get; init; }
    public required string Key { get; init; }
    public required string Value { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required Guid UpdatedByUserId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    ///     When true, the Value holds a secret (a token, a password, an API key). It
    ///     is stored
    ///     whole -- the executor needs the real value -- but the API masks it on the
    ///     way out.
    /// </summary>
    public required bool IsSensitive { get; init; }
}