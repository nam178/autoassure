using System.ComponentModel.DataAnnotations;

namespace A2.Server.Contracts;

/// <summary>Request body to create a new Scenario for an Application. Folder defaults to "/" when
/// not given; Tags default to empty.</summary>
public record CreateScenarioRequest
{
    [Required, MaxLength(200)]
    public required string Title { get; init; }

    [Required, MaxLength(10000)]
    public required string Description { get; init; }

    [MaxLength(300)]
    public string? Folder { get; init; }

    [MaxLength(20)]
    public IReadOnlyList<string>? Tags { get; init; }

    [MaxLength(200)]
    public IReadOnlyList<ActivityRequest>? Activities { get; init; }
}
