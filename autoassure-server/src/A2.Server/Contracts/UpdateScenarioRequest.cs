using System.ComponentModel.DataAnnotations;

namespace A2.Server.Contracts;

/// <summary>Request body to edit an existing Scenario's Title/Description/Folder/Tags/Activities.</summary>
public record UpdateScenarioRequest
{
    [Required, MaxLength(200)]
    public required string Title { get; init; }

    [Required, MaxLength(10000)]
    public required string Description { get; init; }

    [Required, MaxLength(300)]
    public required string Folder { get; init; }

    [MaxLength(20)]
    public IReadOnlyList<string>? Tags { get; init; }

    [MaxLength(200)]
    public IReadOnlyList<ActivityRequest>? Activities { get; init; }
}
