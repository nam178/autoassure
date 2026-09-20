namespace A2.Server.Contracts;

// ReSharper disable UnusedAutoPropertyAccessor.Global -- serialized to the JSON response body, not read in-process
/// <summary>
///     An Environment variable as it was when a Run was created, as returned to
///     the client. When
///     IsSensitive is true, Value is already masked -- more heavily than the live
///     Environment API masks it,
///     since a Run snapshot lives for three years.
/// </summary>
public record RunEnvironmentVariableSnapshotResponse
{
    public required string Key { get; init; }
    public required string Value { get; init; }
    public required bool IsSensitive { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required Guid UpdatedByUserId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
