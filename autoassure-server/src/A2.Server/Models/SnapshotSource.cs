namespace A2.Server.Models;

/// <summary>Where a Run snapshot was copied from: provenance and a navigation hint only, never a live
/// reference. The source row can be edited, archived or deleted after the snapshot is taken, so <see
/// cref="Id"/> MUST NOT be dereferenced for data or condition-checked -- it may no longer resolve to
/// anything. It exists so a UI can offer "open the current version, if it still exists" and so a run can
/// answer "which version of this ran, and who last touched it" without re-reading a row that may be
/// gone.</summary>
public record SnapshotSource
{
    public required Guid Id { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required Guid UpdatedByUserId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
