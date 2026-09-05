using System.ComponentModel.DataAnnotations;
using A2.Server.Common;

namespace A2.Server.Contracts;

/// <summary>Request body to edit an existing Scenario's Title/Description/Folder/Tags.</summary>
public record UpdateScenarioRequest
{
    [Required, NotBlank, MaxLength(200)]
    public required string Title { get; init; }

    [Required, MaxLength(2000)]
    public required string Description { get; init; }

    [Required, MaxLength(300)]
    public required string Folder { get; init; }

    [MaxLength(20)]
    public IReadOnlyList<string>? Tags { get; init; }
}
