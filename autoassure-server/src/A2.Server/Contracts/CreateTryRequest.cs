using System.ComponentModel.DataAnnotations;

namespace A2.Server.Contracts;

/// <summary>Request body to Try a single Scenario against an Environment.</summary>
public record CreateTryRequest
{
    [Required]
    public required Guid EnvironmentId { get; init; }
}
