using A2.Server.Engine.Models;

namespace A2.Server.Engine.Repositories;

/// <summary>
/// The only Environment fields
/// <see cref="IEnvironmentRepository.TryUpdateAsync" /> is allowed to change.
/// </summary>
public record EnvironmentUpdatableFields
{
    public required string Name { get; init; }
    public required EnvironmentClassification Classification { get; init; }
    public required Guid UpdatedByUserId { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
