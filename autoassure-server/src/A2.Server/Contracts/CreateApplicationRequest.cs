using System.ComponentModel.DataAnnotations;

namespace A2.Server.Contracts;

/// <summary>Request body to create a new Application in the caller's Organization.</summary>
public record CreateApplicationRequest
{
    [Required, MaxLength(100)]
    public required string Name { get; init; }

    [Required(AllowEmptyStrings = true), MaxLength(1000)]
    public required string Description { get; init; }
}
