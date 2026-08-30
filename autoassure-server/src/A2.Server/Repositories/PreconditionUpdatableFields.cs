using A2.Server.Models;

namespace A2.Server.Repositories;

/// <summary>The only Precondition fields <see cref="IPreconditionRepository.TryUpdateAsync"/> is allowed to change.</summary>
public record PreconditionUpdatableFields
{
    public required string Name { get; init; }
    public required PreconditionValueSource ValueSource { get; init; }
    public required string ExampleValue { get; init; }
    public required Guid UpdatedByUserId { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
