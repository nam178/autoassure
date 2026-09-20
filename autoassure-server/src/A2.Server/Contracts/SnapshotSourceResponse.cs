namespace A2.Server.Contracts;

// ReSharper disable UnusedAutoPropertyAccessor.Global -- serialized to the JSON response body, not read in-process
/// <summary>
///     Where a Run snapshot was copied from, as returned to the client: provenance
///     only. Id may no
///     longer resolve to a live row -- the source can have been edited, archived
///     or deleted since this Run
///     was created -- so it should be treated as a hint for "open the current
///     version, if it still exists",
///     never as a live reference.
/// </summary>
public record SnapshotSourceResponse
{
    public required Guid Id { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required Guid UpdatedByUserId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
